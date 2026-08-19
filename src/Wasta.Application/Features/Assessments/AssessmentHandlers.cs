using Microsoft.Extensions.Options;
using Wasta.Application.Abstractions;
using Wasta.Application.Common;
using Wasta.Domain.Assessments;

namespace Wasta.Application.Features.Assessments;

/// <summary>
/// Shared failures. An attempt belonging to someone else reports "not found",
/// never "forbidden": a 403 would confirm the attempt exists, which is enough
/// to enumerate other people's attempts by walking ids.
/// </summary>
internal static class AssessmentErrors
{
    public static Result<T> NotFound<T>() =>
        Result.Failure<T>("attempt.not_found", "That attempt does not exist.");

    public static Result NotFound() =>
        Result.Failure("attempt.not_found", "That attempt does not exist.");
}

public class StartAttemptHandler(
    IAssessmentRepository assessments,
    IAttemptRepository attempts,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<StartAttemptResult>> HandleAsync(
        StartAttemptCommand command, CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        var form = await assessments.FindActiveFormAsync(command.TrackId, ct);
        if (form is null)
        {
            return Result.Failure<StartAttemptResult>(
                "assessment.no_active_form", "No assessment is available for this track yet.");
        }

        // Retakes are per track: a seeker may sit Data Science while still
        // cooling down on Frontend.
        var lastStart = await assessments.FindLastAttemptStartAsync(command.SeekerId, command.TrackId, ct);
        if (lastStart is not null)
        {
            var availableAt = lastStart.Value.Add(Attempt.RetakeCooldown);
            if (now < availableAt)
            {
                return Result.Failure<StartAttemptResult>(
                    "assessment.retake_too_soon",
                    $"You can retake this track on {availableAt:yyyy-MM-dd}.");
            }
        }

        var attempt = new Attempt(command.SeekerId, form.FormId, command.TrackId, form.DurationSeconds, now);
        attempts.Add(attempt);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new StartAttemptResult(
            attempt.Id, command.TrackId, attempt.ExpiresAt, form.DurationSeconds, form.QuestionCount));
    }
}

public class GetAttemptHandler(
    IAssessmentRepository assessments,
    IAttemptRepository attempts,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<AttemptView>> HandleAsync(long attemptId, long seekerId, CancellationToken ct = default)
    {
        var attempt = await attempts.FindAsync(attemptId, ct);
        if (attempt is null || attempt.JobSeekerId != seekerId)
        {
            return AssessmentErrors.NotFound<AttemptView>();
        }

        var now = clock.UtcNow;

        // Reading an attempt whose clock ran out settles it, so an abandoned tab
        // does not leave a row sitting in progress forever.
        if (attempt.State == AttemptState.InProgress && attempt.HasExpired(now))
        {
            attempt.MarkExpired();
            await unitOfWork.SaveChangesAsync(ct);
        }

        var questions = await assessments.GetFormQuestionsForDisplayAsync(attempt.FormId, attempt.Id, ct);

        return Result.Success(new AttemptView(
            attempt.Id,
            attempt.State.ToString(),
            attempt.ExpiresAt,
            (int)attempt.RemainingTime(now).TotalSeconds,
            questions.Count,
            questions));
    }
}

public class SaveAnswerHandler(
    IAssessmentRepository assessments,
    IAttemptRepository attempts,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(SaveAnswerCommand command, CancellationToken ct = default)
    {
        var attempt = await attempts.FindAsync(command.AttemptId, ct);
        if (attempt is null || attempt.JobSeekerId != command.SeekerId)
        {
            return AssessmentErrors.NotFound();
        }

        var now = clock.UtcNow;

        if (attempt.State != AttemptState.InProgress)
        {
            return Result.Failure("attempt.not_in_progress", "This attempt has already finished.");
        }

        // The server owns the clock. A paused tab, a tampered countdown, or a
        // replayed request cannot buy extra time.
        if (attempt.HasExpired(now))
        {
            attempt.MarkExpired();
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Failure("attempt.expired", "The time limit for this attempt has passed.");
        }

        if (!await assessments.QuestionBelongsToFormAsync(attempt.FormId, command.QuestionId, ct))
        {
            return AssessmentErrors.NotFound();
        }

        // Stops an answer being pinned to an option from a different question,
        // which would otherwise poison grading.
        if (command.SelectedOptionId is not null
            && !await assessments.OptionBelongsToQuestionAsync(command.QuestionId, command.SelectedOptionId.Value, ct))
        {
            return Result.Failure("answer.option_invalid", "That option does not belong to this question.");
        }

        await attempts.UpsertAnswerAsync(
            attempt.Id, command.QuestionId, command.SelectedOptionId, command.FlaggedForReview, now, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}

public class SubmitAttemptHandler(
    IAssessmentRepository assessments,
    IAttemptRepository attempts,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<AssessmentOptions> options)
{
    public async Task<Result<ResultsView>> HandleAsync(SubmitAttemptCommand command, CancellationToken ct = default)
    {
        var attempt = await attempts.FindWithAnswersAsync(command.AttemptId, ct);
        if (attempt is null || attempt.JobSeekerId != command.SeekerId)
        {
            return AssessmentErrors.NotFound<ResultsView>();
        }

        var now = clock.UtcNow;

        if (attempt.State != AttemptState.InProgress)
        {
            return Result.Failure<ResultsView>("attempt.not_in_progress", "This attempt has already finished.");
        }

        if (attempt.HasExpired(now))
        {
            attempt.MarkExpired();
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Failure<ResultsView>("attempt.expired", "The time limit for this attempt has passed.");
        }

        var ruleVersionId = await assessments.FindActiveRuleVersionIdAsync(attempt.TrackId, ct);
        if (ruleVersionId is null)
        {
            return Result.Failure<ResultsView>(
                "assessment.no_scoring_rules", "This track has no active scoring rules.");
        }

        var formQuestions = await assessments.GetFormQuestionsForGradingAsync(attempt.FormId, ct);
        var answered = attempt.Answers.ToDictionary(a => a.QuestionId, a => a.SelectedOptionId);

        // Every question on the form is graded, not just the answered ones.
        // Skipping a question has to cost the same as getting it wrong, or
        // leaving the hard ones blank would raise the score.
        var graded = formQuestions
            .Select(q => new GradedAnswer(
                q.SectionId,
                q.QuestionId,
                answered.TryGetValue(q.QuestionId, out var selected)
                    && selected is not null
                    && selected == q.CorrectOptionId))
            .ToList();

        var weights = await assessments.GetSectionWeightsAsync(ruleVersionId.Value, ct);
        var bands = await assessments.GetBandsAsync(ruleVersionId.Value, ct);
        var outcome = ScoreCalculator.Calculate(graded, weights, bands);

        var cohort = await assessments.GetCohortScoresAsync(attempt.TrackId, ct);
        var percentile = ScoreCalculator.CalculatePercentile(
            outcome.OverallPercent, cohort, options.Value.MinimumCohortForPercentile);

        attempt.Submit(now);

        attempts.AddScore(new AttemptScore
        {
            AttemptId = attempt.Id,
            RuleVersionId = ruleVersionId.Value,
            OverallPercent = outcome.OverallPercent,
            Percentile = percentile,
            ComputedAt = now,
        });

        foreach (var section in outcome.Sections)
        {
            attempts.AddSectionScore(new AttemptSectionScore
            {
                AttemptId = attempt.Id,
                SectionId = section.SectionId,
                Percent = section.Percent,
                BandId = section.BandId,
            });
        }

        await unitOfWork.SaveChangesAsync(ct);

        var results = await attempts.GetResultsAsync(attempt.Id, ct);
        return results is null
            ? AssessmentErrors.NotFound<ResultsView>()
            : Result.Success(results);
    }
}

public class GetResultsHandler(IAttemptRepository attempts)
{
    public async Task<Result<ResultsView>> HandleAsync(long attemptId, long seekerId, CancellationToken ct = default)
    {
        var attempt = await attempts.FindAsync(attemptId, ct);
        if (attempt is null || attempt.JobSeekerId != seekerId)
        {
            return AssessmentErrors.NotFound<ResultsView>();
        }

        if (attempt.State != AttemptState.Submitted)
        {
            return Result.Failure<ResultsView>("attempt.not_submitted", "This attempt has not been submitted.");
        }

        var results = await attempts.GetResultsAsync(attemptId, ct);
        return results is null ? AssessmentErrors.NotFound<ResultsView>() : Result.Success(results);
    }
}
