using Wasta.Application.Common;
using Wasta.Application.Features.Admin;
using Wasta.Domain.Assessments;
using Wasta.Domain.Catalog;
using Wasta.Domain.Localization;

namespace Wasta.Application.Abstractions;

public interface IAdminContentRepository
{
    // ---- tracks and sections ----
    Task<Track?> FindTrackAsync(int trackId, CancellationToken ct = default);
    Task<bool> TrackSlugExistsAsync(string slug, CancellationToken ct = default);
    void AddTrack(Track track);
    Task<Section?> FindSectionAsync(int sectionId, CancellationToken ct = default);
    Task<IReadOnlyList<int>> SectionIdsForTrackAsync(int trackId, CancellationToken ct = default);
    void AddSection(Section section);

    // ---- questions ----
    Task<Question?> FindQuestionWithOptionsAsync(long questionId, CancellationToken ct = default);
    void AddQuestion(Question question);
    void ReplaceOptions(long questionId, IReadOnlyList<QuestionOption> options);
    Task<IReadOnlyList<long>> ActiveQuestionIdsForTrackAsync(int trackId, CancellationToken ct = default);

    /// <summary>
    /// Whether a submitted attempt has ever been graded against this question.
    /// Once one has, the question is frozen: editing it would change what a past
    /// score meant, and a published score has to stay reproducible.
    /// </summary>
    Task<bool> QuestionIsLockedAsync(long questionId, CancellationToken ct = default);

    // ---- forms ----
    Task<AssessmentForm?> FindFormAsync(int formId, CancellationToken ct = default);
    Task<bool> FormVersionExistsAsync(int trackId, int version, CancellationToken ct = default);
    void AddForm(AssessmentForm form);
    Task<IReadOnlyList<long>> FormQuestionIdsAsync(int formId, CancellationToken ct = default);
    Task ReplaceFormQuestionsAsync(int formId, IReadOnlyList<long> questionIds, CancellationToken ct = default);

    /// <summary>True once any attempt has been opened against the form.</summary>
    Task<bool> FormIsLockedAsync(int formId, CancellationToken ct = default);

    Task DeactivateOtherFormsAsync(int trackId, int keepFormId, CancellationToken ct = default);

    // ---- scoring ----
    Task<ScoringRuleVersion?> FindScoringRuleAsync(int ruleVersionId, CancellationToken ct = default);
    Task<bool> ScoringRuleVersionExistsAsync(int trackId, int version, CancellationToken ct = default);
    void AddScoringRule(ScoringRuleVersion rule);
    Task ReplaceBandsAsync(int ruleVersionId, IReadOnlyList<ScoreBand> bands, CancellationToken ct = default);
    Task ReplaceWeightsAsync(
        int ruleVersionId, IReadOnlyDictionary<int, decimal> weights, CancellationToken ct = default);
    Task<IReadOnlyList<ScoreBand>> BandsForRuleAsync(int ruleVersionId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<int, decimal>> WeightsForRuleAsync(
        int ruleVersionId, CancellationToken ct = default);

    /// <summary>True once a score has been computed with this rule version.</summary>
    Task<bool> ScoringRuleIsLockedAsync(int ruleVersionId, CancellationToken ct = default);

    Task DeactivateOtherScoringRulesAsync(int trackId, int keepRuleId, CancellationToken ct = default);

    Task<ScoreBand?> FindBandAsync(int bandId, CancellationToken ct = default);
    Task UpsertSectionFeedbackAsync(int sectionId, int bandId, string body, CancellationToken ct = default);

    // ---- translations ----
    Task UpsertTranslationAsync(
        string entityType, long entityId, Language language, string value, CancellationToken ct = default);

    // ---- read models ----
    Task<PagedResult<AdminQuestionView>> ListQuestionsAsync(
        int trackId, PageRequest page, CancellationToken ct = default);
    Task<IReadOnlyList<AdminFormView>> ListFormsAsync(int trackId, CancellationToken ct = default);
    Task<IReadOnlyList<AdminScoringRuleView>> ListScoringRulesAsync(
        int trackId, CancellationToken ct = default);
    Task<IReadOnlyList<TrackReadiness>> ReadinessAsync(CancellationToken ct = default);
}

/// <summary>
/// Read side of the admin content surface. Split from the write repository so a
/// listing endpoint does not take a dependency on everything that can mutate.
/// </summary>
public interface IAdminContentQueries
{
    Task<PagedResult<AdminQuestionView>> ListQuestionsAsync(
        int trackId, PageRequest page, CancellationToken ct = default);

    Task<IReadOnlyList<AdminFormView>> ListFormsAsync(int trackId, CancellationToken ct = default);

    Task<IReadOnlyList<AdminScoringRuleView>> ListScoringRulesAsync(
        int trackId, CancellationToken ct = default);
}
