namespace Wasta.Application.Features.Admin;

public sealed record CreateTrackCommand(string Name, string Slug, int DisplayOrder);

public sealed record UpdateTrackCommand(int TrackId, string Name, bool IsActive, int DisplayOrder);

public sealed record CreateSectionCommand(int TrackId, string Name, int DisplayOrder);

public sealed record QuestionOptionInput(string Body, bool IsCorrect, short DisplayOrder);

public sealed record CreateQuestionCommand(
    int TrackId,
    int SectionId,
    string Prompt,
    string? Code,
    string? CodeLanguage,
    short? Difficulty,
    IReadOnlyList<QuestionOptionInput> Options);

public sealed record UpdateQuestionCommand(
    long QuestionId,
    string Prompt,
    string? Code,
    string? CodeLanguage,
    short? Difficulty,
    IReadOnlyList<QuestionOptionInput> Options);

public sealed record CreateFormCommand(int TrackId, int Version, short QuestionCount, int DurationSeconds);

public sealed record SetFormQuestionsCommand(int FormId, IReadOnlyList<long> QuestionIds);

public sealed record CreateScoringRuleCommand(int TrackId, int Version, string? Notes);

public sealed record BandInput(string Name, short MinPercent, short MaxPercent);

public sealed record SetBandsCommand(int RuleVersionId, IReadOnlyList<BandInput> Bands);

public sealed record SetWeightsCommand(int RuleVersionId, IReadOnlyDictionary<int, decimal> Weights);

public sealed record SetSectionFeedbackCommand(int SectionId, int BandId, string Body);

public sealed record SetTranslationCommand(
    string EntityType, long EntityId, string LanguageTag, string Value);

public sealed record AdminQuestionView(
    long QuestionId,
    int TrackId,
    int SectionId,
    string SectionName,
    string Prompt,
    string? Code,
    short? Difficulty,
    bool IsActive,
    bool IsLocked,
    IReadOnlyList<AdminOptionView> Options);

public sealed record AdminOptionView(long OptionId, string Body, bool IsCorrect, short DisplayOrder);

public sealed record AdminFormView(
    int FormId,
    int TrackId,
    int Version,
    short QuestionCount,
    int DurationSeconds,
    bool IsActive,
    int AssignedQuestions,
    bool IsLocked);

public sealed record AdminScoringRuleView(
    int RuleVersionId,
    int TrackId,
    int Version,
    string? Notes,
    bool IsActive,
    bool IsLocked,
    IReadOnlyList<BandInput> Bands,
    IReadOnlyDictionary<int, decimal> Weights);

/// <summary>
/// Readiness of a track's content, so an admin can see what is still missing
/// before trying to activate anything. The seeded placeholders are flagged
/// explicitly: content that exists is not the same as content that is real.
/// </summary>
public sealed record TrackReadiness(
    int TrackId,
    string TrackName,
    int Sections,
    int ActiveQuestions,
    int PlaceholderQuestions,
    int Forms,
    bool HasActiveForm,
    bool HasActiveScoringRule,
    IReadOnlyList<string> Blockers);
