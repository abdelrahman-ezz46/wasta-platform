using Wasta.Domain.Assessments;

namespace Wasta.Domain.Tests;

public class ContentRulesTests
{
    // ---------- options ----------

    [Fact]
    public void A_question_with_one_correct_option_is_fine()
    {
        (string, bool)[] options = [("A", true), ("B", false), ("C", false), ("D", false)];

        Assert.Null(ContentRules.ValidateOptions(options));
    }

    [Fact]
    public void A_question_with_no_correct_option_is_rejected()
    {
        // Unscoreable: every candidate loses the mark, and nobody would notice
        // except as a slightly depressed section average.
        (string, bool)[] options = [("A", false), ("B", false)];

        Assert.Equal("question.no_correct_option", ContentRules.ValidateOptions(options)!.Value.Code);
    }

    [Fact]
    public void A_question_with_two_correct_options_is_rejected()
    {
        // Ambiguous: both answers are right, only one earns the mark.
        (string, bool)[] options = [("A", true), ("B", true), ("C", false)];

        Assert.Equal(
            "question.multiple_correct_options", ContentRules.ValidateOptions(options)!.Value.Code);
    }

    [Fact]
    public void A_blank_option_is_rejected()
    {
        (string, bool)[] options = [("A", true), ("  ", false)];

        Assert.Equal("question.empty_option", ContentRules.ValidateOptions(options)!.Value.Code);
    }

    [Fact]
    public void A_single_option_is_not_a_question()
    {
        (string, bool)[] options = [("Only answer", true)];

        Assert.Equal("question.too_few_options", ContentRules.ValidateOptions(options)!.Value.Code);
    }

    // ---------- bands ----------

    [Fact]
    public void Bands_that_tile_zero_to_one_hundred_are_fine()
    {
        (short, short)[] bands = [(0, 59), (60, 79), (80, 100)];

        Assert.Null(ContentRules.ValidateBands(bands));
    }

    [Fact]
    public void A_gap_between_bands_is_rejected()
    {
        // 60-64 lands in no band, so that student sees no feedback at all for
        // the section.
        (short, short)[] bands = [(0, 59), (65, 100)];

        var violation = ContentRules.ValidateBands(bands)!.Value;
        Assert.Equal("bands.gap", violation.Code);
        Assert.Contains("60-64", violation.Message);
    }

    [Fact]
    public void Overlapping_bands_are_rejected()
    {
        // Which band a 55 gets would depend on row order.
        (short, short)[] bands = [(0, 60), (50, 100)];

        Assert.Equal("bands.overlap", ContentRules.ValidateBands(bands)!.Value.Code);
    }

    [Fact]
    public void Bands_must_start_at_zero_and_reach_one_hundred()
    {
        Assert.Equal("bands.gap_at_start", ContentRules.ValidateBands([((short)5, (short)100)])!.Value.Code);
        Assert.Equal("bands.gap_at_end", ContentRules.ValidateBands([((short)0, (short)95)])!.Value.Code);
    }

    [Fact]
    public void An_inverted_band_is_rejected()
    {
        Assert.Equal("bands.out_of_range", ContentRules.ValidateBands([((short)80, (short)20)])!.Value.Code);
    }

    // ---------- weights ----------

    [Fact]
    public void Weights_summing_to_one_across_every_section_are_fine()
    {
        var weights = new Dictionary<int, decimal> { [1] = 0.5m, [2] = 0.3m, [3] = 0.2m };

        Assert.Null(ContentRules.ValidateWeights(weights, [1, 2, 3]));
    }

    [Fact]
    public void Repeating_fractions_are_accepted_within_tolerance()
    {
        // Three equal sections cannot sum to exactly 1 at four decimal places.
        var weights = new Dictionary<int, decimal> { [1] = 0.3333m, [2] = 0.3333m, [3] = 0.3333m };

        Assert.Null(ContentRules.ValidateWeights(weights, [1, 2, 3]));
    }

    [Fact]
    public void Weights_that_do_not_sum_to_one_are_rejected()
    {
        var weights = new Dictionary<int, decimal> { [1] = 0.5m, [2] = 0.2m };

        Assert.Equal("weights.do_not_sum", ContentRules.ValidateWeights(weights, [1, 2])!.Value.Code);
    }

    [Fact]
    public void A_section_with_no_weight_is_rejected()
    {
        // The calculator renormalises over what it is handed, so an omitted
        // section would vanish from the overall score instead of failing.
        var weights = new Dictionary<int, decimal> { [1] = 0.5m, [2] = 0.5m };

        Assert.Equal("weights.section_missing", ContentRules.ValidateWeights(weights, [1, 2, 3])!.Value.Code);
    }

    [Fact]
    public void A_weight_for_another_tracks_section_is_rejected()
    {
        var weights = new Dictionary<int, decimal> { [1] = 0.5m, [99] = 0.5m };

        Assert.Equal(
            "weights.section_not_on_track", ContentRules.ValidateWeights(weights, [1])!.Value.Code);
    }

    [Fact]
    public void A_negative_weight_is_rejected()
    {
        var weights = new Dictionary<int, decimal> { [1] = 1.5m, [2] = -0.5m };

        Assert.Equal("weights.negative", ContentRules.ValidateWeights(weights, [1, 2])!.Value.Code);
    }

    // ---------- form composition ----------

    [Fact]
    public void A_complete_form_is_fine()
    {
        Assert.Null(ContentRules.ValidateFormComposition(3, [1, 2, 3], [1, 2, 3, 4]));
    }

    [Fact]
    public void A_form_holding_the_wrong_number_of_questions_is_rejected()
    {
        var violation = ContentRules.ValidateFormComposition(30, [1, 2], [1, 2])!.Value;

        Assert.Equal("form.question_count_mismatch", violation.Code);
        Assert.Contains("30", violation.Message);
    }

    [Fact]
    public void A_repeated_question_is_rejected()
    {
        Assert.Equal(
            "form.duplicate_question",
            ContentRules.ValidateFormComposition(3, [1, 2, 2], [1, 2, 3])!.Value.Code);
    }

    [Fact]
    public void A_question_from_another_track_is_rejected()
    {
        Assert.Equal(
            "form.question_not_on_track",
            ContentRules.ValidateFormComposition(2, [1, 99], [1, 2])!.Value.Code);
    }

    [Fact]
    public void An_empty_form_is_rejected()
    {
        Assert.Equal("form.empty", ContentRules.ValidateFormComposition(0, [], [1])!.Value.Code);
    }
}
