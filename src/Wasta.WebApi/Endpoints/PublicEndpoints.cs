using Microsoft.EntityFrameworkCore;
using Wasta.Infrastructure.Persistence;

namespace Wasta.WebApi.Endpoints;

public static class PublicEndpoints
{
    /// <summary>
    /// Aggregate counts for the landing page.
    ///
    /// Anonymous on purpose - these are the numbers a visitor is entitled to
    /// before signing up, and they are counts, never rows: nothing here can be
    /// used to discover who exists. Reading them live rather than hard-coding
    /// them means the page cannot quietly claim traction the platform does not
    /// have.
    /// </summary>
    public static IEndpointRouteBuilder MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/public/stats", async (WastaDbContext db, CancellationToken ct) =>
        {
            var candidates = await db.JobSeekerProfiles.CountAsync(p => p.VisibleToCompanies, ct);
            var companies = await db.Companies.CountAsync(ct);
            var tracks = await db.Tracks.CountAsync(t => t.IsActive, ct);

            return Results.Ok(new { candidates, companies, tracks });
        })
        .AllowAnonymous()
        .WithTags("Public")
        .WithSummary("Counts for the landing page. Aggregates only, never rows.");

        return app;
    }
}
