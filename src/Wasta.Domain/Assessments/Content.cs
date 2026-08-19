using Wasta.Domain.Common;

namespace Wasta.Domain.Assessments;

/// <summary>A scored area within a track. Frontend has Fundamentals, Algorithms, and so on.</summary>
public class Section : Entity<int>
{
    public int TrackId { get; set; }
    public string Name { get; set; } = null!;
    public int DisplayOrder { get; set; }
}

/// <summary>
/// One interchangeable sitting of an assessment. Several active forms per track
/// is what makes a monthly retake meaningful - otherwise a retake is the same
/// thirty questions the seeker has already seen.
/// </summary>
public class AssessmentForm : Entity<int>, ICreatedAt
{
    public int TrackId { get; set; }
    public int Version { get; set; }
    public short QuestionCount { get; set; } = 30;
    public int DurationSeconds { get; set; } = 2700;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Question : Entity<long>, ICreatedAt
{
    public int TrackId { get; set; }
    public int SectionId { get; set; }

    /// <summary>jsonb: prompt plus an optional code block, stored as markdown.</summary>
    public string Body { get; set; } = null!;

    public short? Difficulty { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    public List<QuestionOption> Options { get; set; } = [];
}

public class QuestionOption : Entity<long>
{
    public long QuestionId { get; set; }
    public string Body { get; set; } = null!;
    public bool IsCorrect { get; set; }
    public short DisplayOrder { get; set; }
}

public class AssessmentFormQuestion
{
    public int FormId { get; set; }
    public long QuestionId { get; set; }
    public short DisplayOrder { get; set; }
}
