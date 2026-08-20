using Wasta.Application.Abstractions;
using Wasta.Application.Common;
using Wasta.Domain.Companies;
using Wasta.Domain.Jobs;
using Wasta.Application.Features.Applications;

namespace Wasta.Application.Features.Jobs;

internal static class JobErrors
{
    /// <summary>
    /// Another company's post is "not found", never "forbidden". A 403 confirms
    /// the post exists and lets a competitor map the platform by walking ids.
    /// </summary>
    public static Result<T> NotFound<T>() => Result.Failure<T>("job.not_found", "That job post does not exist.");

    public static Result NotFound() => Result.Failure("job.not_found", "That job post does not exist.");
}

public class PostJobHandler(
    IJobPostRepository jobs,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<long>> HandleAsync(PostJobCommand command, CancellationToken ct = default)
    {
        if (!await jobs.TrackExistsAsync(command.TrackId, ct))
        {
            return Result.Failure<long>("job.track_invalid", "That track does not exist.");
        }

        var active = await jobs.CountActiveAsync(command.CompanyId, ct);
        if (active >= Company.MaxActiveJobPosts)
        {
            return Result.Failure<long>(
                "job.active_limit_reached",
                $"A company may have {Company.MaxActiveJobPosts} active job posts. Close one first.");
        }

        var skillIds = command.SkillIds ?? [];
        if (skillIds.Count > 0 && !await jobs.AllSkillsExistAsync(skillIds, ct))
        {
            return Result.Failure<long>("job.skill_invalid", "One or more skills do not exist.");
        }

        var post = new JobPost(
            command.CompanyId, command.Title, command.TrackId, command.JobDescription, clock.UtcNow)
        {
            WorkTypeId = command.WorkTypeId,
            LocationId = command.LocationId,
            EmploymentTypeId = command.EmploymentTypeId,
            ProjectBrief = command.ProjectBrief,
            ProjectDeadline = command.ProjectDeadline,
        };

        if (command.Salary is { } salary)
        {
            // Throws if min exceeds max, or if an amount arrives without a
            // currency - four currencies are in play, so a bare number is
            // meaningless.
            post.SetSalary(salary.Min, salary.Max, salary.Currency, salary.Period);
        }

        jobs.Add(post);
        await unitOfWork.SaveChangesAsync(ct);

        if (skillIds.Count > 0)
        {
            await jobs.ReplaceSkillsAsync(post.Id, skillIds, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }

        return Result.Success(post.Id);
    }
}

public class UpdateJobHandler(IJobPostRepository jobs, IUnitOfWork unitOfWork)
{
    public async Task<Result> HandleAsync(UpdateJobCommand command, CancellationToken ct = default)
    {
        var post = await jobs.FindAsync(command.JobPostId, ct);
        if (post is null || post.CompanyId != command.CompanyId)
        {
            return JobErrors.NotFound();
        }

        var skillIds = command.SkillIds;
        if (skillIds is { Count: > 0 } && !await jobs.AllSkillsExistAsync(skillIds, ct))
        {
            return Result.Failure("job.skill_invalid", "One or more skills do not exist.");
        }

        post.Update(command.Title, command.JobDescription);
        post.ProjectBrief = command.ProjectBrief;
        post.ProjectDeadline = command.ProjectDeadline;

        if (command.Salary is { } salary)
        {
            post.SetSalary(salary.Min, salary.Max, salary.Currency, salary.Period);
        }

        if (skillIds is not null)
        {
            await jobs.ReplaceSkillsAsync(post.Id, skillIds, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public class CloseJobHandler(IJobPostRepository jobs, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result> HandleAsync(long jobPostId, long companyId, CancellationToken ct = default)
    {
        var post = await jobs.FindAsync(jobPostId, ct);
        if (post is null || post.CompanyId != companyId)
        {
            return JobErrors.NotFound();
        }

        if (!post.IsActive)
        {
            return Result.Failure("job.already_closed", "That job post is already closed.");
        }

        // Closing frees a slot against the cap but keeps the post and its
        // applications intact - applicants must not lose their work.
        post.Close(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}

public class BrowseJobsHandler(IJobQueries queries)
{
    public Task<PagedResult<JobSummary>> HandleAsync(BrowseJobsQuery query, CancellationToken ct = default) =>
        queries.BrowseAsync(query, ct);
}

public class GetJobDetailHandler(IJobQueries queries)
{
    public async Task<Result<JobDetail>> HandleAsync(
        long jobPostId, long? seekerId, CancellationToken ct = default)
    {
        var detail = await queries.GetDetailAsync(jobPostId, seekerId, ct);
        return detail is null ? JobErrors.NotFound<JobDetail>() : Result.Success(detail);
    }
}

public class ListCompanyJobsHandler(IJobQueries queries)
{
    public Task<PagedResult<JobSummary>> HandleAsync(
        long companyId, PageRequest page, CancellationToken ct = default) =>
        queries.ListForCompanyAsync(companyId, page, ct);
}

public class GetJobApplicantsHandler(IJobPostRepository jobs, IJobQueries queries)
{
    public async Task<Result<PagedResult<ApplicantView>>> HandleAsync(
        long jobPostId, long companyId, PageRequest page, CancellationToken ct = default)
    {
        var post = await jobs.FindAsync(jobPostId, ct);
        if (post is null || post.CompanyId != companyId)
        {
            return JobErrors.NotFound<PagedResult<ApplicantView>>();
        }

        return Result.Success(await queries.GetApplicantsAsync(jobPostId, companyId, page, ct));
    }
}
