namespace Wasta.Application.Features.Assessments;

public sealed record StartAttemptCommand(long SeekerId, int TrackId);

public sealed record StartAttemptResult(
    long AttemptId,
    int TrackId,
    DateTimeOffset ExpiresAt,
    int DurationSeconds,
    int QuestionCount);

/// <summary>
/// One option as the candidate sees it. There is deliberately no correctness
/// field: the shape itself is what stops the answer key reaching the browser,
/// rather than remembering to strip it at every call site.
/// </summary>
public sealed record AttemptOptionView(long OptionId, string Body, short DisplayOrder);

public sealed record AttemptQuestionView(
    long QuestionId,
    string Body,
    short DisplayOrder,
    IReadOnlyList<AttemptOptionView> Options,
    long? SelectedOptionId,
    bool FlaggedForReview);

public sealed record AttemptView(
    long AttemptId,
    string State,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    int QuestionCount,
    IReadOnlyList<AttemptQuestionView> Questions);

public sealed record SaveAnswerCommand(
    long AttemptId,
    long SeekerId,
    long QuestionId,
    long? SelectedOptionId,
    bool FlaggedForReview);

public sealed record SubmitAttemptCommand(long AttemptId, long SeekerId);

public sealed record SectionScoreView(
    int SectionId,
    string SectionName,
    short Percent,
    string? BandName,
    string? Feedback);

public sealed record ResultsView(
    long AttemptId,
    int TrackId,
    short OverallPercent,
    short? Percentile,
    DateTimeOffset ComputedAt,
    IReadOnlyList<SectionScoreView> Sections,
    IReadOnlyList<SectionScoreView> SkillGaps);

/// <summary>Everything scoring needs about one question. Server-side only - carries the answer key.</summary>
public sealed record FormQuestionGrading(long QuestionId, int SectionId, long? CorrectOptionId);

public sealed record ActiveFormInfo(int FormId, int TrackId, int DurationSeconds, short QuestionCount);

public sealed class AssessmentOptions
{
    public const string SectionName = "Assessment";

    /// <summary>
    /// Below this many scored attempts on a track, the percentile is withheld
    /// rather than shown. A percentile drawn from a handful of attempts is
    /// noise, and employers spend money on the strength of it.
    /// </summary>
    public int MinimumCohortForPercentile { get; set; } = 50;
}
