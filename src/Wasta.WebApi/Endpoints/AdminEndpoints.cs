using System.Security.Claims;
using Wasta.Application.Common;
using Wasta.Application.Features.Credits;
using Wasta.WebApi.Auth;

namespace Wasta.WebApi.Endpoints;

public static class AdminEndpoints
{
    public sealed record RejectRequest(string Note);

    public sealed record ReviewTopUpRequest(bool Approve, string? Note);

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin")
            .WithTags("Admin")
            .RequireAuthorization(Policies.AdminOnly);

        group.MapGet("/companies/pending", async (
            int? page, int? pageSize, ListPendingCompaniesHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new PageRequest(page, pageSize), ct)))
        .WithSummary("Companies awaiting verification, oldest first.")
        .Produces<PagedResult<PendingCompanyView>>();

        group.MapPost("/companies/{companyId:long}/approve", async (
            long companyId, ClaimsPrincipal user, ApproveCompanyHandler handler, CancellationToken ct) =>
        {
            var adminUserId = user.UserId();
            if (adminUserId is null)
            {
                return Results.Unauthorized();
            }

            return ProblemMapping.ToResponse(
                await handler.HandleAsync(new ApproveCompanyCommand(companyId, adminUserId.Value), ct));
        })
        .WithSummary("Approve a company and grant its trial credits. The two land in one transaction.")
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/companies/{companyId:long}/reject", async (
            long companyId, RejectRequest body, ClaimsPrincipal user,
            RejectCompanyHandler handler, CancellationToken ct) =>
        {
            var adminUserId = user.UserId();
            if (adminUserId is null)
            {
                return Results.Unauthorized();
            }

            return ProblemMapping.ToResponse(
                await handler.HandleAsync(
                    new RejectCompanyCommand(companyId, adminUserId.Value, body.Note), ct));
        })
        .WithSummary("Reject a company with a note explaining what was wrong.");

        group.MapGet("/topups/pending", async (
            int? page, int? pageSize, ListPendingTopUpsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new PageRequest(page, pageSize), ct)))
        .WithSummary("Top-up requests awaiting confirmation that the transfer arrived.")
        .Produces<PagedResult<TopUpRequestView>>();

        group.MapPost("/topups/{requestId:long}/review", async (
            long requestId, ReviewTopUpRequest body, ClaimsPrincipal user,
            ReviewTopUpHandler handler, CancellationToken ct) =>
        {
            var adminUserId = user.UserId();
            if (adminUserId is null)
            {
                return Results.Unauthorized();
            }

            var command = new ReviewTopUpCommand(requestId, adminUserId.Value, body.Approve, body.Note);
            return ProblemMapping.ToResponse(await handler.HandleAsync(command, ct));
        })
        .WithSummary(
            "Confirm or refuse a top-up. Approving issues the credits, and only after a human "
            + "has checked the money actually arrived.")
        .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
