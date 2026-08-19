namespace Wasta.Domain.Assessments;

/// <summary>One graded answer, reduced to what scoring actually needs.</summary>
public readonly record struct GradedAnswer(int SectionId, long QuestionId, bool IsCorrect);

public readonly record struct SectionResult(int SectionId, short Percent, int? BandId);

public sealed record ScoreOutcome(
    short OverallPercent,
    IReadOnlyList<SectionResult> Sections,
    IReadOnlyList<int> SkillGapSectionIds);

/// <summary>
/// Turns graded answers into a score. Pure: no clock, no database, no
/// randomness. That is deliberate - this is the number employers hire on, so it
/// has to be reproducible from its inputs alone, and a rubric change must be
/// replayable against an old attempt.
/// </summary>
public static class ScoreCalculator
{
    /// <summary>How many of the weakest sections the results page calls out as gaps.</summary>
    public const int SkillGapCount = 3;

    /// <param name="answers">Every question on the form, graded. Unanswered questions count as incorrect.</param>
    /// <param name="weights">Section weights from the active rule version. Empty means equal weighting.</param>
    /// <param name="bands">Bands from the same rule version, used to label each section.</param>
    public static ScoreOutcome Calculate(
        IReadOnlyCollection<GradedAnswer> answers,
        IReadOnlyDictionary<int, decimal> weights,
        IReadOnlyCollection<ScoreBand> bands)
    {
        if (answers.Count == 0)
        {
            return new ScoreOutcome(0, [], []);
        }

        var sections = answers
            .GroupBy(a => a.SectionId)
            .Select(group =>
            {
                var total = group.Count();
                var correct = group.Count(a => a.IsCorrect);
                var percent = (short)Math.Round(correct * 100.0 / total, MidpointRounding.AwayFromZero);
                var band = bands.FirstOrDefault(b => b.Contains(percent));

                return new SectionResult(group.Key, percent, band?.Id);
            })
            .OrderBy(s => s.SectionId)
            .ToList();

        var overall = CalculateOverall(sections, weights);

        // Weakest first. Ties break on section id so the same attempt always
        // reports the same gaps rather than shuffling between requests.
        var gaps = sections
            .OrderBy(s => s.Percent)
            .ThenBy(s => s.SectionId)
            .Take(SkillGapCount)
            .Select(s => s.SectionId)
            .ToList();

        return new ScoreOutcome(overall, sections, gaps);
    }

    private static short CalculateOverall(
        IReadOnlyCollection<SectionResult> sections, IReadOnlyDictionary<int, decimal> weights)
    {
        // Only weights for sections actually on this form count, and they are
        // renormalised. Otherwise a rule version that weights a section the form
        // does not include would silently drag every score down.
        var applicable = sections
            .Select(s => (s.Percent, Weight: weights.TryGetValue(s.SectionId, out var w) ? w : 0m))
            .ToList();

        var totalWeight = applicable.Sum(x => x.Weight);

        if (totalWeight <= 0m)
        {
            return (short)Math.Round(sections.Average(s => s.Percent), MidpointRounding.AwayFromZero);
        }

        var weighted = applicable.Sum(x => x.Percent * x.Weight) / totalWeight;
        return (short)Math.Round(weighted, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Percentile of <paramref name="overallPercent"/> against the cohort, as the
    /// share of peers scoring strictly lower.
    ///
    /// Null below <paramref name="minimumCohort"/>: "94th percentile" out of
    /// eleven attempts is a number the results page would be lying with, and
    /// employers make decisions on it.
    /// </summary>
    public static short? CalculatePercentile(
        short overallPercent, IReadOnlyCollection<short> cohortScores, int minimumCohort)
    {
        if (cohortScores.Count < minimumCohort)
        {
            return null;
        }

        var below = cohortScores.Count(s => s < overallPercent);
        return (short)Math.Round(below * 100.0 / cohortScores.Count, MidpointRounding.AwayFromZero);
    }
}
