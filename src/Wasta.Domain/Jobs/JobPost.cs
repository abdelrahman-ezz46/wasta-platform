using Wasta.Domain.Common;

namespace Wasta.Domain.Jobs;

public class JobPost : Entity<long>, ICreatedAt
{
    private JobPost() { }

    public JobPost(long companyId, string title, int trackId, string jobDescription, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("job.title_required", "A job title is required.");
        }

        CompanyId = companyId;
        Title = title.Trim();
        TrackId = trackId;
        JobDescription = jobDescription;
        IsActive = true;
        CreatedAt = now;
    }

    public long CompanyId { get; private set; }
    public string Title { get; private set; } = null!;
    public int TrackId { get; private set; }
    public int? WorkTypeId { get; set; }
    public int? LocationId { get; set; }
    public int? EmploymentTypeId { get; set; }
    public decimal? SalaryMin { get; private set; }
    public decimal? SalaryMax { get; private set; }

    /// <summary>ISO 4217. Salaries span EGP, AED, JOD and SAR, so a bare number is meaningless.</summary>
    public string? SalaryCurrency { get; private set; }

    public string? SalaryPeriod { get; private set; }
    public string JobDescription { get; private set; } = null!;
    public string? ProjectBrief { get; set; }
    public DateOnly? ProjectDeadline { get; set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ClosesAt { get; set; }

    public void SetSalary(decimal? min, decimal? max, string? currency, string? period)
    {
        if (min is not null && max is not null && min > max)
        {
            throw new DomainException("job.salary_range_invalid", "Minimum salary cannot exceed the maximum.");
        }

        if ((min is not null || max is not null) && string.IsNullOrWhiteSpace(currency))
        {
            throw new DomainException("job.salary_currency_required", "A salary needs a currency.");
        }

        SalaryMin = min;
        SalaryMax = max;
        SalaryCurrency = currency?.ToUpperInvariant();
        SalaryPeriod = period;
    }

    public void Update(string title, string jobDescription)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("job.title_required", "A job title is required.");
        }

        Title = title.Trim();
        JobDescription = jobDescription;
    }

    public void Close(DateTimeOffset now)
    {
        IsActive = false;
        ClosesAt = now;
    }
}

public class JobPostSkill
{
    public long JobPostId { get; set; }
    public int SkillId { get; set; }
}

public class JobPostFile : Entity<long>, ICreatedAt
{
    public long JobPostId { get; set; }
    public string FileUrl { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}
