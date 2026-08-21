using Microsoft.EntityFrameworkCore;
using Wasta.SupportChat.Domain;
using Wasta.Infrastructure.Persistence;

namespace Wasta.WebApi.Integration;

/// <summary>
/// Real job posts for the chatbot to mention.
///
/// Track-matched when the visitor is a signed-in seeker, most recent otherwise.
/// Only live postings, and only fields the module already declares - it costs
/// tokens on every turn a listing is offered, so there is no point sending more
/// than it can use.
/// </summary>
public sealed class PlatformJobListingProvider(WastaDbContext db) : IJobListingProvider
{
    public async Task<IReadOnlyList<JobListing>> GetOpenListingsAsync(
        int? studentId, int maxResults, CancellationToken ct)
    {
        int? trackId = studentId is null
            ? null
            : await db.JobSeekers.AsNoTracking()
                .Where(s => s.Id == studentId).Select(s => s.TrackId).FirstOrDefaultAsync(ct);

        var query = db.JobPosts.AsNoTracking().Where(j => j.IsActive);

        if (trackId is not null)
        {
            query = query.Where(j => j.TrackId == trackId);
        }

        var rows = await query
            .OrderByDescending(j => j.CreatedAt)
            .Take(Math.Clamp(maxResults, 1, 20))
            .Select(j => new
            {
                j.Id,
                j.Title,
                CompanyName = db.Companies.Where(c => c.Id == j.CompanyId).Select(c => c.Name).First(),
                TrackName = db.Tracks.Where(t => t.Id == j.TrackId).Select(t => t.Name).FirstOrDefault(),
                City = db.Locations.Where(l => l.Id == j.LocationId).Select(l => l.City).FirstOrDefault(),
                Skills = db.JobPostSkills
                    .Where(s => s.JobPostId == j.Id)
                    .Join(db.Skills, s => s.SkillId, s => s.Id, (_, skill) => skill.Name)
                    .ToList(),
            })
            .ToListAsync(ct);

        return rows.Select(r => new JobListing(
            r.Title,
            r.CompanyName,
            r.TrackName,
            r.Skills,

            // The chatbot is told never to invent a URL. Giving it a real
            // relative path means it has one it can actually use.
            r.City,
            $"/jobs/{r.Id}")).ToList();
    }
}
