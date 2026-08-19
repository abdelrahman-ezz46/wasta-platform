using Wasta.Domain.Common;

namespace Wasta.Domain.Assessments;

/// <summary>
/// Versioned so a score computed last year stays reproducible after the rubric
/// changes. Attempts record which version scored them.
/// </summary>
public class ScoringRuleVersion : Entity<int>, ICreatedAt
{
    public int TrackId { get; set; }
    public int Version { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class SectionWeight
{
    public int RuleVersionId { get; set; }
    public int SectionId { get; set; }
    public decimal Weight { get; set; }
}

public class ScoreBand : Entity<int>
{
    public int RuleVersionId { get; set; }
    public string Name { get; set; } = null!;
    public short MinPercent { get; set; }
    public short MaxPercent { get; set; }

    public bool Contains(short percent) => percent >= MinPercent && percent <= MaxPercent;
}

/// <summary>Fixed copy shown instantly on the results page. Identical for everyone in a band.</summary>
public class SectionBandFeedback
{
    public int SectionId { get; set; }
    public int BandId { get; set; }
    public string Body { get; set; } = null!;
}
