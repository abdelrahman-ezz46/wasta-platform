using System.Security.Claims;
using Wasta.Application.Features.Me;
using Wasta.WebApi.Auth;

namespace Wasta.WebApi.Endpoints;

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/seekers/me", async (ClaimsPrincipal user, IMeQueries queries, CancellationToken ct) =>
        {
            var seekerId = user.SeekerId();
            if (seekerId is null)
            {
                return Results.NotFound();
            }

            var summary = await queries.GetSeekerAsync(seekerId.Value, ct);

            // 404 rather than 403 throughout. A 403 confirms the row exists,
            // which turns any id-taking endpoint into an enumeration oracle.
            return summary is null ? Results.NotFound() : Results.Ok(summary);
        })
        .RequireAuthorization(Policies.SeekerOnly)
        .WithTags("Me")
        .WithSummary("The signed-in job seeker's own summary.")
        .Produces<SeekerSummary>();

        app.MapGet("/api/companies/me", async (ClaimsPrincipal user, IMeQueries queries, CancellationToken ct) =>
        {
            var companyId = user.CompanyId();
            if (companyId is null)
            {
                return Results.NotFound();
            }

            var summary = await queries.GetCompanyAsync(companyId.Value, ct);
            return summary is null ? Results.NotFound() : Results.Ok(summary);
        })
        .RequireAuthorization(Policies.CompanyOnly)
        .WithTags("Me")
        .WithSummary("The signed-in company's own summary, including verification state.")
        .Produces<CompanySummary>();

        app.MapGet("/api/companies/me/credits", async (ClaimsPrincipal user, IMeQueries queries, CancellationToken ct) =>
        {
            var companyId = user.CompanyId();
            return companyId is null
                ? Results.NotFound()
                : Results.Ok(await queries.GetCreditBalanceAsync(companyId.Value, ct));
        })
        // Verified only: an unapproved company must not reach anything that
        // spends or reveals. Credits are the door to the talent pool.
        .RequireAuthorization(Policies.VerifiedCompanyOnly)
        .WithTags("Me")
        .WithSummary("Credit balance, summed from the ledger. Verified companies only.")
        .Produces<CreditBalance>();

        return app;
    }


}
