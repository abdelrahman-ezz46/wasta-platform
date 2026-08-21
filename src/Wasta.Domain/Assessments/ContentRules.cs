namespace Wasta.Domain.Assessments;

public readonly record struct RuleViolation(string Code, string Message);

/// <summary>
/// What has to be true of assessment content before it can go live.
///
/// These are here rather than in a validator because they are the rules that
/// decide whether a score means anything. Content that passes a schema check
/// but breaks one of these produces numbers that look fine and are wrong -
/// bands with a gap silently drop a student into no band at all, weights that
/// do not sum leave the overall score on a different scale to the sections.
/// </summary>
public static class ContentRules
{
    public const int MinOptionsPerQuestion = 2;
    public const int MaxOptionsPerQuestion = 6;

    /// <summary>Weights are compared to 1 within this, to absorb rounding on repeating fractions like 1/3.</summary>
    public const decimal WeightTolerance = 0.005m;

    /// <summary>
    /// Exactly one option may be correct. Zero makes the question unscoreable
    /// and every candidate loses the mark; more than one makes it ambiguous and
    /// only one of the right answers earns it.
    /// </summary>
    public static RuleViolation? ValidateOptions(IReadOnlyCollection<(string Body, bool IsCorrect)> options)
    {
        if (options.Count < MinOptionsPerQuestion)
        {
            return new RuleViolation(
                "question.too_few_options", $"A question needs at least {MinOptionsPerQuestion} options.");
        }

        if (options.Count > MaxOptionsPerQuestion)
        {
            return new RuleViolation(
                "question.too_many_options", $"A question may have at most {MaxOptionsPerQuestion} options.");
        }

        if (options.Any(o => string.IsNullOrWhiteSpace(o.Body)))
        {
            return new RuleViolation("question.empty_option", "An option cannot be blank.");
        }

        var correct = options.Count(o => o.IsCorrect);

        return correct switch
        {
            0 => new RuleViolation(
                "question.no_correct_option", "Exactly one option must be marked correct; none is."),
            > 1 => new RuleViolation(
                "question.multiple_correct_options",
                $"Exactly one option must be marked correct; {correct} are."),
            _ => null,
        };
    }

    /// <summary>
    /// Bands must tile 0-100 exactly: no gap, no overlap.
    ///
    /// A gap means a score lands in no band and the student sees no feedback for
    /// that section. An overlap means which band they get depends on row order,
    /// so the same score can be labelled differently on two different days.
    /// </summary>
    public static RuleViolation? ValidateBands(IReadOnlyCollection<(short Min, short Max)> bands)
    {
        if (bands.Count == 0)
        {
            return new RuleViolation("bands.none", "At least one band is required.");
        }

        foreach (var (min, max) in bands)
        {
            if (min < 0 || max > 100 || min > max)
            {
                return new RuleViolation(
                    "bands.out_of_range", $"Band {min}-{max} is not a valid range within 0-100.");
            }
        }

        var ordered = bands.OrderBy(b => b.Min).ToList();

        if (ordered[0].Min != 0)
        {
            return new RuleViolation("bands.gap_at_start", "Bands must start at 0.");
        }

        if (ordered[^1].Max != 100)
        {
            return new RuleViolation("bands.gap_at_end", "Bands must reach 100.");
        }

        for (var i = 1; i < ordered.Count; i++)
        {
            var previous = ordered[i - 1];
            var current = ordered[i];

            if (current.Min <= previous.Max)
            {
                return new RuleViolation(
                    "bands.overlap",
                    $"Bands {previous.Min}-{previous.Max} and {current.Min}-{current.Max} overlap.");
            }

            if (current.Min != previous.Max + 1)
            {
                return new RuleViolation(
                    "bands.gap",
                    $"Nothing covers {previous.Max + 1}-{current.Min - 1}.");
            }
        }

        return null;
    }

    /// <summary>
    /// Section weights must sum to 1 and cover every section on the track.
    ///
    /// A missing section is worse than a wrong weight: the calculator
    /// renormalises over what it is given, so an omitted section silently
    /// disappears from the overall score rather than failing loudly.
    /// </summary>
    public static RuleViolation? ValidateWeights(
        IReadOnlyDictionary<int, decimal> weights, IReadOnlyCollection<int> trackSectionIds)
    {
        if (weights.Count == 0)
        {
            return new RuleViolation("weights.none", "Every section needs a weight.");
        }

        var missing = trackSectionIds.Where(id => !weights.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            return new RuleViolation(
                "weights.section_missing",
                $"No weight given for section(s) {string.Join(", ", missing)}.");
        }

        var extra = weights.Keys.Where(id => !trackSectionIds.Contains(id)).ToList();
        if (extra.Count > 0)
        {
            return new RuleViolation(
                "weights.section_not_on_track",
                $"Section(s) {string.Join(", ", extra)} are not on this track.");
        }

        if (weights.Values.Any(w => w < 0))
        {
            return new RuleViolation("weights.negative", "A weight cannot be negative.");
        }

        var total = weights.Values.Sum();
        if (Math.Abs(total - 1m) > WeightTolerance)
        {
            return new RuleViolation("weights.do_not_sum", $"Weights sum to {total:0.###}, not 1.");
        }

        return null;
    }

    /// <summary>
    /// A form is only publishable when it holds exactly the number of questions
    /// it claims, all from its own track, with no repeats.
    /// </summary>
    public static RuleViolation? ValidateFormComposition(
        int declaredQuestionCount,
        IReadOnlyCollection<long> questionIds,
        IReadOnlyCollection<long> questionIdsOnTrack)
    {
        if (questionIds.Count == 0)
        {
            return new RuleViolation("form.empty", "A form needs questions.");
        }

        if (questionIds.Distinct().Count() != questionIds.Count)
        {
            return new RuleViolation("form.duplicate_question", "The same question appears twice.");
        }

        var offTrack = questionIds.Where(id => !questionIdsOnTrack.Contains(id)).ToList();
        if (offTrack.Count > 0)
        {
            return new RuleViolation(
                "form.question_not_on_track",
                $"Question(s) {string.Join(", ", offTrack)} are not on this track.");
        }

        if (questionIds.Count != declaredQuestionCount)
        {
            return new RuleViolation(
                "form.question_count_mismatch",
                $"This form declares {declaredQuestionCount} questions but holds {questionIds.Count}.");
        }

        return null;
    }
}
