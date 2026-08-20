using System.Security.Claims;
using Wasta.Application.Common;
using Wasta.Application.Features.Credits;
using Wasta.Application.Features.TalentPool;
using Wasta.WebApi.Auth;
using Wasta.WebApi;

namespace Wasta.WebApi.Endpoints;

public static class TalentPoolEndpoints
{
    public sealed record RequestTopUpRequest(
        int CreditsRequested, int PaymentMethodId, decimal? Amount, string? Currency);

    public static IEndpointRouteBuilder MapTalentPoolEndpoints(this IEndpointRouteBuilder app)
    {
        // Verified companies only, throughout. The talent pool is the product;
        // an unapproved company must not see inside it.
        var pool = app.MapGroup("/api/talent-pool")
            .WithTags("Talent pool")
            .RequireAuthorization(Policies.VerifiedCompanyOnly);

        pool.MapGet("/", async (
            int? trackId, short? minScore, string? skillIds, string? sort, int? page, int? pageSize,
            ClaimsPrincipal user, BrowseTalentPoolHandler handler, CancellationToken ct) =>
        {
            var companyId = user.CompanyId();
            if (companyId is null)
            {
                return Results.NotFound();
            }

            var parsedSkills = skillIds?
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var id) ? id : (int?)null)
                .Where(id => id is not null)
                .Select(id => id!.Value)
                .ToList();

            var query = new BrowseTalentPoolQuery(
                companyId.Value, trackId, minScore, parsedSkills, sort, page, pageSize);

            return Results.Ok(await handler.HandleAsync(query, ct));
        })
        .WithSummary("Browse candidates, score-ranked. Identities are withheld until unlocked.")
        .Produces<PagedResult<TalentPoolCandidate>>();

        pool.MapGet("/{seekerId:long}", async (
            long seekerId, ClaimsPrincipal user, GetCandidateHandler handler, CancellationToken ct) =>
        {
            var companyId = user.CompanyId();
            return companyId is null
                ? Results.NotFound()
                : ProblemMapping.ToResponse(await handler.HandleAsync(companyId.Value, seekerId, ct));
        })
        .WithSummary("One candidate. Name, email, phone and CV appear only once unlocked.")
        .Produces<CandidateDetail>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        pool.MapPost("/{seekerId:long}/unlock", async (
            long seekerId, ClaimsPrincipal user, UnlockCandidateHandler handler, CancellationToken ct) =>
        {
            var companyId = user.CompanyId();
            var actorUserId = user.UserId();

            if (companyId is null || actorUserId is null)
            {
                return Results.NotFound();
            }

            var result = await handler.HandleAsync(companyId.Value, seekerId, actorUserId.Value, ct);
            return ProblemMapping.ToResponse(result);
        })
        .RequireRateLimiting(RateLimiting.UnlockPolicy)
        .WithSummary(
            "Spend one credit to reveal a candidate. Idempotent: unlocking an already-unlocked "
            + "candidate returns the existing unlock and charges nothing.")
        .Produces<UnlockResult>()
        .ProducesProblem(StatusCodes.Status409Conflict);

        var credits = app.MapGroup("/api/companies/me/credits")
            .WithTags("Credits")
            .RequireAuthorization(Policies.VerifiedCompanyOnly);

        credits.MapGet("/ledger", async (
            int? page, int? pageSize, ClaimsPrincipal user, GetLedgerHandler handler, CancellationToken ct) =>
        {
            var companyId = user.CompanyId();
            return companyId is null
                ? Results.NotFound()
                : Results.Ok(await handler.HandleAsync(companyId.Value, new PageRequest(page, pageSize), ct));
        })
        .WithSummary("Every credit movement: trial grants, top-ups, unlocks, refunds.")
        .Produces<PagedResult<LedgerEntryView>>();

        credits.MapGet("/topups", async (
            int? page, int? pageSize, ClaimsPrincipal user,
            ListMyTopUpRequestsHandler handler, CancellationToken ct) =>
        {
            var companyId = user.CompanyId();
            return companyId is null
                ? Results.NotFound()
                : Results.Ok(await handler.HandleAsync(companyId.Value, new PageRequest(page, pageSize), ct));
        })
        .WithSummary("The company's own top-up requests and where each one stands.")
        .Produces<PagedResult<TopUpRequestView>>();

        credits.MapPost("/topups", async (
            RequestTopUpRequest body, ClaimsPrincipal user,
            RequestTopUpHandler handler, CancellationToken ct) =>
        {
            var companyId = user.CompanyId();
            if (companyId is null)
            {
                return Results.NotFound();
            }

            var command = new RequestTopUpCommand(
                companyId.Value, body.CreditsRequested, body.PaymentMethodId, body.Amount, body.Currency);

            var result = await handler.HandleAsync(command, ct);
            return result.IsSuccess
                ? Results.Accepted($"/api/companies/me/credits/topups", new { requestId = result.Value })
                : ProblemMapping.ToProblem(result.Error);
        })
        .WithSummary(
            "Ask for credits. No money moves here - the transfer happens out of band and an "
            + "admin issues the credits once it has landed.")
        .Produces(StatusCodes.Status202Accepted);

        return app;
    }
}
