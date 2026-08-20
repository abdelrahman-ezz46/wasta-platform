using Microsoft.EntityFrameworkCore;
using Wasta.Application.Abstractions;
using Wasta.Application.Common;
using Wasta.Application.Features.TalentPool;
using Wasta.Domain.Assessments;
using Wasta.Domain.Catalog;

namespace Wasta.Infrastructure.Persistence.Repositories;

public sealed class TalentPoolQueries(WastaDbContext db) : ITalentPoolQueries
{
    /// <summary>
    /// Candidates eligible to appear: opted in, and holding at least one scored
    /// attempt. A profile with no score has nothing for a company to judge, and
    /// showing it invites an unlock that cannot pay off.
    /// </summary>
    private sealed class PoolRow
    {
        public long SeekerId { get; init; }
        public int? TrackId { get; init; }
        public string? TrackName { get; init; }
        public short? OverallPercent { get; init; }
        public short? Percentile { get; init; }
        public bool IsUnlocked { get; init; }
    }

    private IQueryable<PoolRow> PoolRows(long companyId) =>
        from s in db.JobSeekers.AsNoTracking()
        join p in db.JobSeekerProfiles.AsNoTracking() on s.Id equals p.JobSeekerId
        where p.VisibleToCompanies
        select new PoolRow
        {
            SeekerId = s.Id,
            TrackId = s.TrackId,
            TrackName = db.Tracks.Where(t => t.Id == s.TrackId).Select(t => t.Name).FirstOrDefault(),
            OverallPercent = (from a in db.Attempts
                              join sc in db.AttemptScores on a.Id equals sc.AttemptId
                              where a.JobSeekerId == s.Id
                                    && a.TrackId == s.TrackId
                                    && a.State == AttemptState.Submitted
                              orderby sc.OverallPercent descending
                              select (short?)sc.OverallPercent).FirstOrDefault(),
            Percentile = (from a in db.Attempts
                          join sc in db.AttemptScores on a.Id equals sc.AttemptId
                          where a.JobSeekerId == s.Id
                                && a.TrackId == s.TrackId
                                && a.State == AttemptState.Submitted
                          orderby sc.OverallPercent descending
                          select sc.Percentile).FirstOrDefault(),
            IsUnlocked = db.ProfileUnlocks.Any(u => u.CompanyId == companyId && u.JobSeekerId == s.Id),
        };

    public async Task<PagedResult<TalentPoolCandidate>> BrowseAsync(
        BrowseTalentPoolQuery query, CancellationToken ct = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var rows = PoolRows(query.CompanyId).Where(r => r.OverallPercent != null);

        if (query.TrackId is not null)
        {
            rows = rows.Where(r => r.TrackId == query.TrackId);
        }

        if (query.MinScore is not null)
        {
            rows = rows.Where(r => r.OverallPercent >= query.MinScore);
        }

        if (query.SkillIds is { Count: > 0 })
        {
            // Every requested skill must be present, not merely one of them -
            // "React and TypeScript" should not match someone who only has React.
            var required = query.SkillIds.Distinct().ToList();
            rows = rows.Where(r =>
                db.JobSeekerSkills.Count(js => js.JobSeekerId == r.SeekerId && required.Contains(js.SkillId))
                    == required.Count);
        }

        var total = await rows.CountAsync(ct);

        var ordered = query.Sort?.ToLowerInvariant() switch
        {
            "recent" => rows.OrderByDescending(r => r.SeekerId),
            _ => rows.OrderByDescending(r => r.OverallPercent).ThenBy(r => r.SeekerId),
        };

        var pageRows = await ordered.Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        var seekerIds = pageRows.Select(r => r.SeekerId).ToList();

        var skills = await db.JobSeekerSkills.AsNoTracking()
            .Where(js => seekerIds.Contains(js.JobSeekerId))
            .Join(db.Skills.AsNoTracking(), js => js.SkillId, sk => sk.Id,
                (js, sk) => new { js.JobSeekerId, sk.Name })
            .ToListAsync(ct);

        var projects = await db.JobApplications.AsNoTracking()
            .Where(a => seekerIds.Contains(a.JobSeekerId) && a.ProjectTitle != null)
            .Select(a => new { a.JobSeekerId, a.ProjectTitle })
            .ToListAsync(ct);

        var items = pageRows.Select(r => new TalentPoolCandidate(
            r.SeekerId,
            CandidateReference.For(r.SeekerId),
            r.TrackId,
            r.TrackName,
            r.OverallPercent,
            r.Percentile,
            skills.Where(s => s.JobSeekerId == r.SeekerId).Select(s => s.Name).OrderBy(n => n).ToList(),
            projects.Where(p => p.JobSeekerId == r.SeekerId).Select(p => p.ProjectTitle!).Distinct().ToList(),
            r.IsUnlocked)).ToList();

        return new PagedResult<TalentPoolCandidate>(items, page.Page, page.PageSize, total);
    }

    public async Task<CandidateDetail?> GetCandidateAsync(
        long companyId, long jobSeekerId, CancellationToken ct = default)
    {
        var row = await PoolRows(companyId).FirstOrDefaultAsync(r => r.SeekerId == jobSeekerId, ct);
        if (row is null)
        {
            return null;
        }

        var identity = await (
            from s in db.JobSeekers.AsNoTracking()
            join u in db.UserAccounts.AsNoTracking() on s.UserId equals u.Id
            join p in db.JobSeekerProfiles.AsNoTracking() on s.Id equals p.JobSeekerId
            where s.Id == jobSeekerId
            select new { s.FullName, u.Email, s.PhoneNumber, p.University, p.CvUrl })
            .FirstAsync(ct);

        var skills = await db.JobSeekerSkills.AsNoTracking()
            .Where(js => js.JobSeekerId == jobSeekerId)
            .Join(db.Skills.AsNoTracking(), js => js.SkillId, sk => sk.Id, (_, sk) => sk.Name)
            .OrderBy(n => n)
            .ToListAsync(ct);

        var bestAttemptId = await (
            from a in db.Attempts.AsNoTracking()
            join sc in db.AttemptScores.AsNoTracking() on a.Id equals sc.AttemptId
            where a.JobSeekerId == jobSeekerId && a.State == AttemptState.Submitted
            orderby sc.OverallPercent descending
            select (long?)a.Id).FirstOrDefaultAsync(ct);

        var sections = bestAttemptId is null
            ? []
            : await (
                from ss in db.AttemptSectionScores.AsNoTracking()
                join sec in db.Sections.AsNoTracking() on ss.SectionId equals sec.Id
                where ss.AttemptId == bestAttemptId
                orderby sec.DisplayOrder
                select new SectionScoreLine(
                    ss.SectionId,
                    sec.Name,
                    ss.Percent,
                    db.ScoreBands.Where(b => b.Id == ss.BandId).Select(b => b.Name).FirstOrDefault()))
                .ToListAsync(ct);

        var projects = await db.JobApplications.AsNoTracking()
            .Where(a => a.JobSeekerId == jobSeekerId && a.ProjectTitle != null)
            .OrderByDescending(a => a.SubmittedAt)
            .Select(a => new CandidateProject(
                a.ProjectTitle, a.Description, a.RepoUrl, a.LiveDemoUrl, a.SubmittedAt))
            .ToListAsync(ct);

        // The identity fields are attached only behind the unlock check. The
        // shape stays the same either way so the client does not branch, but
        // nothing identifying is in the payload until a credit has been spent.
        return new CandidateDetail(
            row.SeekerId,
            CandidateReference.For(row.SeekerId),
            row.TrackId,
            row.TrackName,
            row.OverallPercent,
            row.Percentile,
            skills,
            sections,
            projects,
            row.IsUnlocked,
            row.IsUnlocked ? identity.FullName : null,
            row.IsUnlocked ? identity.Email : null,
            row.IsUnlocked ? identity.PhoneNumber : null,
            row.IsUnlocked ? identity.University : null,
            row.IsUnlocked ? identity.CvUrl : null);
    }
}
