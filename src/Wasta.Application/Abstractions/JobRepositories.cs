using Wasta.Application.Common;
using Wasta.Application.Features.Applications;
using Wasta.Application.Features.Jobs;
using Wasta.Domain.Jobs;

namespace Wasta.Application.Abstractions;

public interface IJobPostRepository
{
    Task<JobPost?> FindAsync(long jobPostId, CancellationToken ct = default);

    /// <summary>Counts live postings, for the per-company cap.</summary>
    Task<int> CountActiveAsync(long companyId, CancellationToken ct = default);

    void Add(JobPost jobPost);

    Task ReplaceSkillsAsync(long jobPostId, IReadOnlyList<int> skillIds, CancellationToken ct = default);

    Task<bool> AllSkillsExistAsync(IReadOnlyList<int> skillIds, CancellationToken ct = default);

    Task<bool> TrackExistsAsync(int trackId, CancellationToken ct = default);
}

public interface IJobQueries
{
    Task<PagedResult<JobSummary>> BrowseAsync(BrowseJobsQuery query, CancellationToken ct = default);

    Task<JobDetail?> GetDetailAsync(long jobPostId, long? seekerId, CancellationToken ct = default);

    Task<PagedResult<JobSummary>> ListForCompanyAsync(
        long companyId, PageRequest page, CancellationToken ct = default);

    Task<PagedResult<ApplicantView>> GetApplicantsAsync(
        long jobPostId, long companyId, PageRequest page, CancellationToken ct = default);
}

public interface IJobApplicationRepository
{
    Task<Domain.Applications.JobApplication?> FindAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// Live applications only. The cap counts these rather than all rows, or a
    /// seeker who applied and withdrew six times would be locked out for good.
    /// </summary>
    Task<int> CountLiveAsync(long seekerId, CancellationToken ct = default);

    void Add(Domain.Applications.JobApplication application);

    Task<PagedResult<ApplicationView>> ListForSeekerAsync(
        long seekerId, PageRequest page, CancellationToken ct = default);

    Task<ApplicationView?> GetForSeekerAsync(
        long applicationId, long seekerId, CancellationToken ct = default);

    /// <summary>Which company owns the post this application is against, for the ownership check.</summary>
    Task<long?> FindOwningCompanyIdAsync(long applicationId, CancellationToken ct = default);

    Task<bool> StatusExistsAsync(int statusId, CancellationToken ct = default);
}
