using Wasta.Application.Features.Assessments;
using Wasta.Domain.Assessments;

namespace Wasta.Application.Abstractions;

public interface IAssessmentRepository
{
    Task<ActiveFormInfo?> FindActiveFormAsync(int trackId, CancellationToken ct = default);

    /// <summary>The seeker's most recent attempt start on a track, for the retake cooldown.</summary>
    Task<DateTimeOffset?> FindLastAttemptStartAsync(long seekerId, int trackId, CancellationToken ct = default);

    Task<IReadOnlyList<AttemptQuestionView>> GetFormQuestionsForDisplayAsync(
        int formId, long attemptId, CancellationToken ct = default);

    /// <summary>Includes the answer key. Never reachable from an endpoint response.</summary>
    Task<IReadOnlyList<FormQuestionGrading>> GetFormQuestionsForGradingAsync(
        int formId, CancellationToken ct = default);

    Task<bool> QuestionBelongsToFormAsync(int formId, long questionId, CancellationToken ct = default);

    Task<bool> OptionBelongsToQuestionAsync(long questionId, long optionId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<int, decimal>> GetSectionWeightsAsync(int ruleVersionId, CancellationToken ct = default);

    Task<IReadOnlyList<ScoreBand>> GetBandsAsync(int ruleVersionId, CancellationToken ct = default);

    Task<int?> FindActiveRuleVersionIdAsync(int trackId, CancellationToken ct = default);

    Task<IReadOnlyList<short>> GetCohortScoresAsync(int trackId, CancellationToken ct = default);
}

public interface IAttemptRepository
{
    Task<Attempt?> FindAsync(long attemptId, CancellationToken ct = default);

    Task<Attempt?> FindWithAnswersAsync(long attemptId, CancellationToken ct = default);

    void Add(Attempt attempt);

    Task UpsertAnswerAsync(
        long attemptId, long questionId, long? selectedOptionId, bool flagged, DateTimeOffset now,
        CancellationToken ct = default);

    void AddScore(AttemptScore score);

    void AddSectionScore(AttemptSectionScore sectionScore);

    Task<ResultsView?> GetResultsAsync(long attemptId, CancellationToken ct = default);
}
