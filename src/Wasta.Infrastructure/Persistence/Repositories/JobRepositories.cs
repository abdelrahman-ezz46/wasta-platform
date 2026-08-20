using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Wasta.Application.Abstractions;
using Wasta.Application.Common;
using Wasta.Application.Features.Applications;
using Wasta.Application.Features.Jobs;
using Wasta.Domain.Applications;
using Wasta.Domain.Catalog;
using Wasta.Domain.Jobs;

namespace Wasta.Infrastructure.Persistence.Repositories;

/// <summary>
/// The public handle for an unlocked-not-yet candidate, matching the designs'
/// "#A4F2". Derived by hashing the id rather than encoding it, so the reference
/// is stable across requests without being reversible into a seeker id.
/// </summary>
internal static class CandidateReference
{
    public static string For(long seekerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"wasta-candidate:{seekerId}"));
        return "#" + Convert.ToHexString(hash)[..4];
    }
}

public sealed class JobPostRepository(WastaDbContext db) : IJobPostRepository
{
    public Task<JobPost?> FindAsync(long jobPostId, CancellationToken ct = default) =>
        db.JobPosts.FirstOrDefaultAsync(j => j.Id == jobPostId, ct);

    public Task<int> CountActiveAsync(long companyId, CancellationToken ct = default) =>
        db.JobPosts.CountAsync(j => j.CompanyId == companyId && j.IsActive, ct);

    public void Add(JobPost jobPost) => db.JobPosts.Add(jobPost);

    public async Task ReplaceSkillsAsync(
        long jobPostId, IReadOnlyList<int> skillIds, CancellationToken ct = default)
    {
        var existing = await db.JobPostSkills.Where(s => s.JobPostId == jobPostId).ToListAsync(ct);
        db.JobPostSkills.RemoveRange(existing);

        db.JobPostSkills.AddRange(skillIds.Distinct().Select(id => new JobPostSkill
        {
            JobPostId = jobPostId,
            SkillId = id,
        }));
    }

    public async Task<bool> AllSkillsExistAsync(IReadOnlyList<int> skillIds, CancellationToken ct = default)
    {
        var distinct = skillIds.Distinct().ToList();
        var found = await db.Skills.CountAsync(s => distinct.Contains(s.Id), ct);
        return found == distinct.Count;
    }

    public Task<bool> TrackExistsAsync(int trackId, CancellationToken ct = default) =>
        db.Tracks.AnyAsync(t => t.Id == trackId, ct);
}

public sealed class JobQueries(WastaDbContext db) : IJobQueries
{
    private IQueryable<JobSummaryRow> SummaryRows(long? seekerId) =>
        from j in db.JobPosts.AsNoTracking()
        join c in db.Companies.AsNoTracking() on j.CompanyId equals c.Id
        join t in db.Tracks.AsNoTracking() on j.TrackId equals t.Id
        select new JobSummaryRow
        {
            Id = j.Id,
            Title = j.Title,
            CompanyId = j.CompanyId,
            CompanyName = c.Name,
            TrackId = j.TrackId,
            TrackName = t.Name,
            City = db.Locations.Where(l => l.Id == j.LocationId).Select(l => l.City).FirstOrDefault(),
            CountryCode = db.Locations.Where(l => l.Id == j.LocationId).Select(l => l.CountryCode).FirstOrDefault(),
            WorkType = db.WorkTypes.Where(w => w.Id == j.WorkTypeId).Select(w => w.Name).FirstOrDefault(),
            EmploymentType = db.EmploymentTypes.Where(e => e.Id == j.EmploymentTypeId).Select(e => e.Name).FirstOrDefault(),
            SalaryMin = j.SalaryMin,
            SalaryMax = j.SalaryMax,
            SalaryCurrency = j.SalaryCurrency,
            SalaryPeriod = j.SalaryPeriod,
            IsActive = j.IsActive,
            CreatedAt = j.CreatedAt,
            ApplicantCount = db.JobApplications.Count(a => a.JobPostId == j.Id),
            HasApplied = seekerId != null
                && db.JobApplications.Any(a =>
                    a.JobPostId == j.Id
                    && a.JobSeekerId == seekerId
                    && a.StatusId != ApplicationStatuses.Withdrawn),
        };

    private sealed class JobSummaryRow
    {
        public long Id { get; init; }
        public string Title { get; init; } = null!;
        public long CompanyId { get; init; }
        public string CompanyName { get; init; } = null!;
        public int TrackId { get; init; }
        public string TrackName { get; init; } = null!;
        public string? City { get; init; }
        public string? CountryCode { get; init; }
        public string? WorkType { get; init; }
        public string? EmploymentType { get; init; }
        public decimal? SalaryMin { get; init; }
        public decimal? SalaryMax { get; init; }
        public string? SalaryCurrency { get; init; }
        public string? SalaryPeriod { get; init; }
        public bool IsActive { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public int ApplicantCount { get; init; }
        public bool HasApplied { get; init; }
    }

    private async Task<JobSummary> ToSummaryAsync(JobSummaryRow row, int? seekerTrackId, CancellationToken ct)
    {
        var skills = await db.JobPostSkills.AsNoTracking()
            .Where(s => s.JobPostId == row.Id)
            .Join(db.Skills.AsNoTracking(), s => s.SkillId, s => s.Id, (_, skill) => skill.Name)
            .OrderBy(n => n)
            .ToListAsync(ct);

        var salary = row.SalaryMin is null && row.SalaryMax is null
            ? null
            : new SalaryRange(row.SalaryMin, row.SalaryMax, row.SalaryCurrency?.Trim(), row.SalaryPeriod);

        return new JobSummary(
            row.Id, row.Title, row.CompanyName, row.TrackId, row.TrackName,
            row.City, row.CountryCode?.Trim(), row.WorkType, row.EmploymentType,
            salary, skills, row.IsActive, row.CreatedAt,
            IsRecommended: seekerTrackId is not null && seekerTrackId == row.TrackId,
            HasApplied: row.HasApplied,
            ApplicantCount: row.ApplicantCount);
    }

    public async Task<PagedResult<JobSummary>> BrowseAsync(BrowseJobsQuery query, CancellationToken ct = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);

        int? seekerTrackId = query.SeekerId is null
            ? null
            : await db.JobSeekers.AsNoTracking()
                .Where(s => s.Id == query.SeekerId)
                .Select(s => s.TrackId)
                .FirstOrDefaultAsync(ct);

        // Closed postings never appear in browse. They stay reachable by id so
        // an existing applicant can still see what they applied to.
        var rows = SummaryRows(query.SeekerId).Where(r => r.IsActive);

        if (query.TrackId is not null)
        {
            rows = rows.Where(r => r.TrackId == query.TrackId);
        }

        if (query.RecommendedOnly && seekerTrackId is not null)
        {
            rows = rows.Where(r => r.TrackId == seekerTrackId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            rows = rows.Where(r =>
                EF.Functions.ILike(r.Title, term)
                || EF.Functions.ILike(r.CompanyName, term)
                || EF.Functions.ILike(r.TrackName, term));
        }

        var total = await rows.CountAsync(ct);

        var pageRows = await rows
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(ct);

        var items = new List<JobSummary>(pageRows.Count);
        foreach (var row in pageRows)
        {
            items.Add(await ToSummaryAsync(row, seekerTrackId, ct));
        }

        // Recommended first within the page, so the seeker's own track leads.
        items = [.. items.OrderByDescending(i => i.IsRecommended).ThenByDescending(i => i.CreatedAt)];

        return new PagedResult<JobSummary>(items, page.Page, page.PageSize, total);
    }

    public async Task<JobDetail?> GetDetailAsync(
        long jobPostId, long? seekerId, CancellationToken ct = default)
    {
        var row = await SummaryRows(seekerId).FirstOrDefaultAsync(r => r.Id == jobPostId, ct);
        if (row is null)
        {
            return null;
        }

        int? seekerTrackId = seekerId is null
            ? null
            : await db.JobSeekers.AsNoTracking()
                .Where(s => s.Id == seekerId).Select(s => s.TrackId).FirstOrDefaultAsync(ct);

        var extra = await db.JobPosts.AsNoTracking()
            .Where(j => j.Id == jobPostId)
            .Select(j => new { j.JobDescription, j.ProjectBrief, j.ProjectDeadline })
            .FirstAsync(ct);

        return new JobDetail(
            await ToSummaryAsync(row, seekerTrackId, ct),
            extra.JobDescription,
            extra.ProjectBrief,
            extra.ProjectDeadline);
    }

    public async Task<PagedResult<JobSummary>> ListForCompanyAsync(
        long companyId, PageRequest page, CancellationToken ct = default)
    {
        var rows = SummaryRows(null).Where(r => r.CompanyId == companyId);
        var total = await rows.CountAsync(ct);

        var pageRows = await rows
            .OrderByDescending(r => r.IsActive)
            .ThenByDescending(r => r.CreatedAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(ct);

        var items = new List<JobSummary>(pageRows.Count);
        foreach (var row in pageRows)
        {
            items.Add(await ToSummaryAsync(row, null, ct));
        }

        return new PagedResult<JobSummary>(items, page.Page, page.PageSize, total);
    }

    public async Task<PagedResult<ApplicantView>> GetApplicantsAsync(
        long jobPostId, long companyId, PageRequest page, CancellationToken ct = default)
    {
        var query =
            from a in db.JobApplications.AsNoTracking()
            join st in db.ApplicationStatuses.AsNoTracking() on a.StatusId equals st.Id
            join s in db.JobSeekers.AsNoTracking() on a.JobSeekerId equals s.Id
            where a.JobPostId == jobPostId
            select new
            {
                a.Id,
                a.JobSeekerId,
                a.StatusId,
                StatusName = st.Name,
                a.ProjectTitle,
                a.RepoUrl,
                a.LiveDemoUrl,
                a.SubmittedAt,
                a.CreatedAt,
                SeekerName = s.FullName,

                // Best submitted score on the seeker's own track.
                Score = (from at in db.Attempts
                         join sc in db.AttemptScores on at.Id equals sc.AttemptId
                         where at.JobSeekerId == a.JobSeekerId && at.TrackId == s.TrackId
                         orderby sc.OverallPercent descending
                         select new { sc.OverallPercent, sc.Percentile }).FirstOrDefault(),

                IsUnlocked = db.ProfileUnlocks.Any(
                    u => u.CompanyId == companyId && u.JobSeekerId == a.JobSeekerId),
            };

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(r => r.SubmittedAt != null)
            .ThenByDescending(r => r.CreatedAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(ct);

        // The name is attached only when this company has actually paid to
        // unlock this candidate. Everyone else gets the reference handle.
        var items = rows.Select(r => new ApplicantView(
            r.Id,
            CandidateReference.For(r.JobSeekerId),
            r.Score?.OverallPercent,
            r.Score?.Percentile,
            r.StatusId,
            r.StatusName,
            r.ProjectTitle,
            r.RepoUrl,
            r.LiveDemoUrl,
            r.SubmittedAt,
            r.IsUnlocked,
            r.IsUnlocked ? r.SeekerName : null)).ToList();

        return new PagedResult<ApplicantView>(items, page.Page, page.PageSize, total);
    }
}

public sealed class JobApplicationRepository(WastaDbContext db) : IJobApplicationRepository
{
    public Task<JobApplication?> FindAsync(long applicationId, CancellationToken ct = default) =>
        db.JobApplications.FirstOrDefaultAsync(a => a.Id == applicationId, ct);

    public Task<int> CountLiveAsync(long seekerId, CancellationToken ct = default) =>
        db.JobApplications.CountAsync(
            a => a.JobSeekerId == seekerId && a.StatusId != ApplicationStatuses.Withdrawn, ct);

    public void Add(JobApplication application) => db.JobApplications.Add(application);

    /// <summary>
    /// Kept in terms of entities. ApplicationView is a positional record, and EF
    /// cannot see through a constructor call - filtering or ordering on an
    /// already-projected record fails to translate at runtime, so the projection
    /// has to be the last step.
    /// </summary>
    private IQueryable<JobApplication> BaseQuery(long seekerId) =>
        db.JobApplications.AsNoTracking().Where(a => a.JobSeekerId == seekerId);

    private IQueryable<ApplicationView> Project(IQueryable<JobApplication> source) =>
        from a in source
        join j in db.JobPosts.AsNoTracking() on a.JobPostId equals j.Id
        join c in db.Companies.AsNoTracking() on j.CompanyId equals c.Id
        join st in db.ApplicationStatuses.AsNoTracking() on a.StatusId equals st.Id
        select new ApplicationView(
            a.Id,
            a.JobPostId,
            j.Title,
            c.Name,
            a.StatusId,
            st.Name,
            a.ProjectTitle,
            a.Description,
            a.RepoUrl,
            a.LiveDemoUrl,
            a.Feedback,
            j.ProjectDeadline,
            a.SubmittedAt,
            a.CreatedAt);

    public async Task<PagedResult<ApplicationView>> ListForSeekerAsync(
        long seekerId, PageRequest page, CancellationToken ct = default)
    {
        var query = BaseQuery(seekerId);
        var total = await query.CountAsync(ct);

        var ordered = query
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Skip(page.Skip)
            .Take(page.PageSize);

        var items = await Project(ordered).ToListAsync(ct);

        return new PagedResult<ApplicationView>(items, page.Page, page.PageSize, total);
    }

    public Task<ApplicationView?> GetForSeekerAsync(
        long applicationId, long seekerId, CancellationToken ct = default) =>
        Project(BaseQuery(seekerId).Where(a => a.Id == applicationId)).FirstOrDefaultAsync(ct);

    /// <summary>Ownership runs through the job post, so it is read from the post, not the application.</summary>
    public async Task<long?> FindOwningCompanyIdAsync(long applicationId, CancellationToken ct = default) =>
        await (from a in db.JobApplications.AsNoTracking()
               join j in db.JobPosts.AsNoTracking() on a.JobPostId equals j.Id
               where a.Id == applicationId
               select (long?)j.CompanyId)
            .FirstOrDefaultAsync(ct);

    public Task<bool> StatusExistsAsync(int statusId, CancellationToken ct = default) =>
        db.ApplicationStatuses.AnyAsync(s => s.Id == statusId, ct);
}
