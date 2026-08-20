namespace Wasta.Application.Features.Applications;

public sealed record ApplyToJobCommand(long SeekerId, long JobPostId);

public sealed record UpdateProjectCommand(
    long ApplicationId,
    long SeekerId,
    string? ProjectTitle,
    string? Description,
    string? RepoUrl,
    string? LiveDemoUrl);

public sealed record SetApplicationStatusCommand(
    long ApplicationId,
    long CompanyId,
    int StatusId,
    string? Feedback);

public sealed record ApplicationView(
    long ApplicationId,
    long JobPostId,
    string JobTitle,
    string CompanyName,
    int StatusId,
    string StatusName,
    string? ProjectTitle,
    string? Description,
    string? RepoUrl,
    string? LiveDemoUrl,
    string? Feedback,
    DateOnly? ProjectDeadline,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset CreatedAt);

/// <summary>
/// An applicant as the company sees them before paying to unlock: the score and
/// the work, never the name. Identity stays behind the credit.
/// </summary>
public sealed record ApplicantView(
    long ApplicationId,
    string CandidateReference,
    short? OverallPercent,
    short? Percentile,
    int StatusId,
    string StatusName,
    string? ProjectTitle,
    string? RepoUrl,
    string? LiveDemoUrl,
    DateTimeOffset? SubmittedAt,
    bool IsUnlocked,
    string? FullName);
