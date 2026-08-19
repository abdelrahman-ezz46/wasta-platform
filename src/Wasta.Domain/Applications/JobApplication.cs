using Wasta.Domain.Catalog;
using Wasta.Domain.Common;

namespace Wasta.Domain.Applications;

/// <summary>
/// An application and the project attached to it are the same row. Applying
/// twice to the same post creates a second application deliberately - a seeker
/// may reapply with better work - so the project cap counts only applications
/// that are still live, or someone who applied and withdrew six times would be
/// locked out forever.
/// </summary>
public class JobApplication : Entity<long>, ICreatedAt
{
    public const int MaxLiveApplications = 6;
    public const int MaxDescriptionLength = 600;

    private JobApplication() { }

    public JobApplication(long jobSeekerId, long jobPostId, DateTimeOffset now)
    {
        JobSeekerId = jobSeekerId;
        JobPostId = jobPostId;
        StatusId = ApplicationStatuses.Applied;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public long JobSeekerId { get; private set; }
    public long JobPostId { get; private set; }
    public int StatusId { get; private set; }
    public string? ProjectTitle { get; private set; }
    public string? Description { get; private set; }
    public string? RepoUrl { get; private set; }
    public string? LiveDemoUrl { get; private set; }
    public string? Feedback { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsWithdrawn => StatusId == ApplicationStatuses.Withdrawn;

    public void UpdateProject(string? title, string? description, string? repoUrl, string? liveDemoUrl, DateTimeOffset now)
    {
        if (description is { Length: > MaxDescriptionLength })
        {
            throw new DomainException(
                "application.description_too_long",
                $"Description must be {MaxDescriptionLength} characters or fewer.");
        }

        ProjectTitle = title;
        Description = description;
        RepoUrl = repoUrl;
        LiveDemoUrl = liveDemoUrl;
        UpdatedAt = now;
    }

    public void SubmitProject(DateTimeOffset now)
    {
        if (IsWithdrawn)
        {
            throw new DomainException("application.withdrawn", "This application has been withdrawn.");
        }

        SubmittedAt = now;
        UpdatedAt = now;
    }

    public void Withdraw(DateTimeOffset now)
    {
        StatusId = ApplicationStatuses.Withdrawn;
        UpdatedAt = now;
    }

    /// <summary>Company-side review. Feedback is optional but travels with the state change.</summary>
    public void SetStatus(int statusId, string? feedback, DateTimeOffset now)
    {
        StatusId = statusId;
        if (feedback is not null)
        {
            Feedback = feedback;
        }

        UpdatedAt = now;
    }
}

public class ApplicationFile : Entity<long>, ICreatedAt
{
    public long ApplicationId { get; set; }
    public string FileUrl { get; set; } = null!;
    public string? FileName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
