using Wasta.Application.Abstractions;
using Wasta.Application.Common;
using Wasta.Domain.Applications;
using Wasta.Application.Features.Notifications;
using Wasta.Domain.Catalog;

namespace Wasta.Application.Features.Applications;

internal static class ApplicationErrors
{
    public static Result<T> NotFound<T>() =>
        Result.Failure<T>("application.not_found", "That application does not exist.");

    public static Result NotFound() =>
        Result.Failure("application.not_found", "That application does not exist.");
}

public class ApplyToJobHandler(
    IJobPostRepository jobs,
    IJobApplicationRepository applications,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<long>> HandleAsync(ApplyToJobCommand command, CancellationToken ct = default)
    {
        var post = await jobs.FindAsync(command.JobPostId, ct);
        if (post is null)
        {
            return Result.Failure<long>("job.not_found", "That job post does not exist.");
        }

        if (!post.IsActive)
        {
            return Result.Failure<long>("job.closed", "That job post is no longer accepting applications.");
        }

        // Counts live applications only. A seeker who applied and withdrew six
        // times would otherwise be locked out permanently.
        var live = await applications.CountLiveAsync(command.SeekerId, ct);
        if (live >= JobApplication.MaxLiveApplications)
        {
            return Result.Failure<long>(
                "application.limit_reached",
                $"You can have {JobApplication.MaxLiveApplications} live applications. Withdraw one first.");
        }

        // Re-applying deliberately creates a NEW application rather than reusing
        // the previous one, so earlier submitted work is preserved alongside the
        // new attempt.
        var application = new JobApplication(command.SeekerId, command.JobPostId, clock.UtcNow);
        applications.Add(application);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(application.Id);
    }
}

public class UpdateProjectHandler(
    IJobApplicationRepository applications,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(UpdateProjectCommand command, CancellationToken ct = default)
    {
        var application = await applications.FindAsync(command.ApplicationId, ct);
        if (application is null || application.JobSeekerId != command.SeekerId)
        {
            return ApplicationErrors.NotFound();
        }

        if (application.IsWithdrawn)
        {
            return Result.Failure("application.withdrawn", "This application has been withdrawn.");
        }

        application.UpdateProject(
            command.ProjectTitle, command.Description, command.RepoUrl, command.LiveDemoUrl, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public class SubmitProjectHandler(
    IJobApplicationRepository applications,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(long applicationId, long seekerId, CancellationToken ct = default)
    {
        var application = await applications.FindAsync(applicationId, ct);
        if (application is null || application.JobSeekerId != seekerId)
        {
            return ApplicationErrors.NotFound();
        }

        application.SubmitProject(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public class WithdrawApplicationHandler(
    IJobApplicationRepository applications,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(long applicationId, long seekerId, CancellationToken ct = default)
    {
        var application = await applications.FindAsync(applicationId, ct);
        if (application is null || application.JobSeekerId != seekerId)
        {
            return ApplicationErrors.NotFound();
        }

        if (application.IsWithdrawn)
        {
            return Result.Failure("application.already_withdrawn", "This application is already withdrawn.");
        }

        // Frees a slot against the seeker's cap; the row stays so the company
        // keeps its record of what happened.
        application.Withdraw(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public class ListMyApplicationsHandler(IJobApplicationRepository applications)
{
    public Task<PagedResult<ApplicationView>> HandleAsync(
        long seekerId, PageRequest page, CancellationToken ct = default) =>
        applications.ListForSeekerAsync(seekerId, page, ct);
}

public class GetMyApplicationHandler(IJobApplicationRepository applications)
{
    public async Task<Result<ApplicationView>> HandleAsync(
        long applicationId, long seekerId, CancellationToken ct = default)
    {
        var view = await applications.GetForSeekerAsync(applicationId, seekerId, ct);
        return view is null ? ApplicationErrors.NotFound<ApplicationView>() : Result.Success(view);
    }
}

public class SetApplicationStatusHandler(
    IJobApplicationRepository applications,
    IUnitOfWork unitOfWork,
    IClock clock,
    INotificationService notifications,
    INotificationRecipients recipients)
{
    public async Task<Result> HandleAsync(SetApplicationStatusCommand command, CancellationToken ct = default)
    {
        var application = await applications.FindAsync(command.ApplicationId, ct);
        if (application is null)
        {
            return ApplicationErrors.NotFound();
        }

        // Ownership runs through the job post: a company may only review
        // applications made to its own postings. Checked against the database,
        // never inferred from the route.
        var owningCompanyId = await applications.FindOwningCompanyIdAsync(command.ApplicationId, ct);
        if (owningCompanyId != command.CompanyId)
        {
            return ApplicationErrors.NotFound();
        }

        if (application.IsWithdrawn)
        {
            return Result.Failure("application.withdrawn", "This application has been withdrawn.");
        }

        // Withdrawal belongs to the seeker. A company marking someone withdrawn
        // would be putting words in their mouth.
        if (command.StatusId == ApplicationStatuses.Withdrawn)
        {
            return Result.Failure(
                "application.status_not_allowed", "Only the applicant can withdraw an application.");
        }

        if (!await applications.StatusExistsAsync(command.StatusId, ct))
        {
            return Result.Failure("application.status_invalid", "That status does not exist.");
        }

        var recipientId = await recipients.UserIdForSeekerAsync(application.JobSeekerId, ct);

        // Read before mutating, and resolve the new status name separately. The
        // view query is AsNoTracking, so reading it after SetStatus but before
        // SaveChanges returns the old status - which would put the wrong status
        // in the notification.
        var view = recipientId is null
            ? null
            : await applications.GetForSeekerAsync(command.ApplicationId, application.JobSeekerId, ct);

        var newStatusName = recipientId is null
            ? null
            : await applications.StatusNameAsync(command.StatusId, ct);

        application.SetStatus(command.StatusId, command.Feedback, clock.UtcNow);

        if (recipientId is not null)
        {
            notifications.Queue(
                recipientId.Value,
                NotificationKinds.ApplicationStatusChanged,
                new
                {
                    applicationId = command.ApplicationId,
                    jobTitle = view?.JobTitle ?? string.Empty,
                    companyName = view?.CompanyName ?? string.Empty,
                    status = newStatusName ?? string.Empty,
                });
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
