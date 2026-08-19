using Wasta.Domain.Assessments;

namespace Wasta.Domain.Tests;

public class ScoreCalculatorTests
{
    private static ScoreBand Band(int id, short min, short max) =>
        new() { Name = $"band-{id}", MinPercent = min, MaxPercent = max };

    private static readonly ScoreBand[] NoBands = [];
    private static readonly Dictionary<int, decimal> NoWeights = [];

    [Fact]
    public void No_answers_scores_zero_rather_than_throwing()
    {
        var outcome = ScoreCalculator.Calculate([], NoWeights, NoBands);

        Assert.Equal(0, outcome.OverallPercent);
        Assert.Empty(outcome.Sections);
    }

    [Fact]
    public void Section_percent_is_correct_over_total()
    {
        GradedAnswer[] answers =
        [
            new(1, 1, true), new(1, 2, true), new(1, 3, true), new(1, 4, false),
        ];

        var outcome = ScoreCalculator.Calculate(answers, NoWeights, NoBands);

        Assert.Equal(75, outcome.Sections.Single().Percent);
    }

    [Fact]
    public void Unweighted_overall_is_the_average_of_sections_not_of_questions()
    {
        // Section 1: 1 of 1 = 100%. Section 2: 1 of 3 = 33%.
        // Averaging sections gives 67; averaging raw questions would give 50.
        // Sections must carry equal weight or a section with more questions
        // quietly counts for more.
        GradedAnswer[] answers =
        [
            new(1, 1, true),
            new(2, 2, true), new(2, 3, false), new(2, 4, false),
        ];

        var outcome = ScoreCalculator.Calculate(answers, NoWeights, NoBands);

        Assert.Equal(67, outcome.OverallPercent);
    }

    [Fact]
    public void Weights_shift_the_overall_score()
    {
        GradedAnswer[] answers = [new(1, 1, true), new(2, 2, false)];
        var weights = new Dictionary<int, decimal> { [1] = 0.75m, [2] = 0.25m };

        var outcome = ScoreCalculator.Calculate(answers, weights, NoBands);

        // 100 * 0.75 + 0 * 0.25 = 75, against 50 unweighted.
        Assert.Equal(75, outcome.OverallPercent);
    }

    [Fact]
    public void Weights_for_sections_not_on_the_form_are_renormalised_away()
    {
        GradedAnswer[] answers = [new(1, 1, true), new(2, 2, false)];

        // Section 3 is weighted but absent. Without renormalising, the divisor
        // would include its weight and drag every score down.
        var weights = new Dictionary<int, decimal> { [1] = 0.25m, [2] = 0.25m, [3] = 0.50m };

        var outcome = ScoreCalculator.Calculate(answers, weights, NoBands);

        Assert.Equal(50, outcome.OverallPercent);
    }

    [Fact]
    public void Each_section_is_labelled_with_the_band_containing_its_score()
    {
        var low = Band(1, 0, 59);
        var high = Band(2, 60, 100);
        ScoreBand[] bands = [low, high];

        GradedAnswer[] answers =
        [
            new(1, 1, true), new(1, 2, true),
            new(2, 3, false), new(2, 4, false),
        ];

        var outcome = ScoreCalculator.Calculate(answers, NoWeights, bands);

        Assert.Equal(high.Id, outcome.Sections.Single(s => s.SectionId == 1).BandId);
        Assert.Equal(low.Id, outcome.Sections.Single(s => s.SectionId == 2).BandId);
    }

    [Fact]
    public void Skill_gaps_are_the_three_weakest_sections_weakest_first()
    {
        GradedAnswer[] answers =
        [
            new(1, 1, true),                    // 100%
            new(2, 2, false),                   // 0%
            new(3, 3, true), new(3, 4, false),  // 50%
            new(4, 5, false), new(4, 6, false), // 0%
        ];

        var outcome = ScoreCalculator.Calculate(answers, NoWeights, NoBands);

        Assert.Equal(3, outcome.SkillGapSectionIds.Count);
        Assert.Equal([2, 4, 3], outcome.SkillGapSectionIds);
        Assert.DoesNotContain(1, outcome.SkillGapSectionIds);
    }

    [Fact]
    public void Percentile_is_suppressed_below_the_minimum_cohort()
    {
        short[] cohort = [10, 20, 30];

        Assert.Null(ScoreCalculator.CalculatePercentile(25, cohort, minimumCohort: 50));
    }

    [Fact]
    public void Percentile_is_the_share_of_the_cohort_scoring_lower()
    {
        short[] cohort = [10, 20, 30, 40];

        Assert.Equal((short?)75, ScoreCalculator.CalculatePercentile(35, cohort, minimumCohort: 4));
    }

    [Fact]
    public void The_lowest_score_in_the_cohort_is_the_zeroth_percentile()
    {
        short[] cohort = [10, 20, 30, 40];

        Assert.Equal((short?)0, ScoreCalculator.CalculatePercentile(10, cohort, minimumCohort: 4));
    }
}
