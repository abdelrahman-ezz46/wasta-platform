namespace Wasta.Application.Features.Jobs;

public sealed record SalaryRange(decimal? Min, decimal? Max, string? Currency, string? Period);

public sealed record PostJobCommand(
    long CompanyId,
    string Title,
    int TrackId,
    string JobDescription,
    int? WorkTypeId,
    int? LocationId,
    int? EmploymentTypeId,
    SalaryRange? Salary,
    string? ProjectBrief,
    DateOnly? ProjectDeadline,
    IReadOnlyList<int>? SkillIds);

public sealed record UpdateJobCommand(
    long JobPostId,
    long CompanyId,
    string Title,
    string JobDescription,
    SalaryRange? Salary,
    string? ProjectBrief,
    DateOnly? ProjectDeadline,
    IReadOnlyList<int>? SkillIds);

public sealed record JobSummary(
    long JobPostId,
    string Title,
    string CompanyName,
    int TrackId,
    string TrackName,
    string? City,
    string? CountryCode,
    string? WorkType,
    string? EmploymentType,
    SalaryRange? Salary,
    IReadOnlyList<string> Skills,
    bool IsActive,
    DateTimeOffset CreatedAt,
    /// <summary>True when the post's track matches the seeker's own. Drives the "Recommended" badge.</summary>
    bool IsRecommended,
    /// <summary>Whether this seeker already has a live application. The client shows "Applied".</summary>
    bool HasApplied,
    int ApplicantCount);

public sealed record JobDetail(
    JobSummary Summary,
    string JobDescription,
    string? ProjectBrief,
    DateOnly? ProjectDeadline);

public sealed record BrowseJobsQuery(
    long? SeekerId,
    int? TrackId,
    string? Search,
    bool RecommendedOnly,
    int? Page,
    int? PageSize);
