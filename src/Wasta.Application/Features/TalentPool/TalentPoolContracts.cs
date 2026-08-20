namespace Wasta.Application.Features.TalentPool;

/// <summary>
/// A candidate as they appear before being unlocked. The seeker id is here
/// because the company needs a handle to unlock with, and it grants nothing on
/// its own: every field that identifies a person is withheld until a credit is
/// spent, and browsing the pool already shows the same set.
/// </summary>
public sealed record TalentPoolCandidate(
    long SeekerId,
    string CandidateReference,
    int? TrackId,
    string? TrackName,
    short? OverallPercent,
    short? Percentile,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> ProjectTitles,
    bool IsUnlocked);

public sealed record SectionScoreLine(int SectionId, string SectionName, short Percent, string? BandName);

/// <summary>
/// The full candidate view. Name, email, phone, university and CV are populated
/// only when this company holds an unlock for this candidate - the shape is the
/// same either way so the client does not branch, but the values are absent.
/// </summary>
public sealed record CandidateDetail(
    long SeekerId,
    string CandidateReference,
    int? TrackId,
    string? TrackName,
    short? OverallPercent,
    short? Percentile,
    IReadOnlyList<string> Skills,
    IReadOnlyList<SectionScoreLine> Sections,
    IReadOnlyList<CandidateProject> Projects,
    bool IsUnlocked,
    string? FullName,
    string? Email,
    string? PhoneNumber,
    string? University,
    string? CvUrl);

public sealed record CandidateProject(
    string? Title,
    string? Description,
    string? RepoUrl,
    string? LiveDemoUrl,
    DateTimeOffset? SubmittedAt);

public sealed record BrowseTalentPoolQuery(
    long CompanyId,
    int? TrackId,
    short? MinScore,
    IReadOnlyList<int>? SkillIds,
    string? Sort,
    int? Page,
    int? PageSize);

public enum UnlockOutcome
{
    Unlocked = 1,

    /// <summary>Already held. Returned without charging again.</summary>
    AlreadyUnlocked = 2,

    InsufficientCredits = 3,

    /// <summary>No such candidate, or they have opted out of the pool.</summary>
    CandidateNotFound = 4,
}

public sealed record UnlockResult(UnlockOutcome Outcome, long? UnlockId, int BalanceAfter);

/// <summary>
/// Spending a credit and recording the unlock, atomically.
///
/// The interface is deliberately coarse: the whole operation has to be one
/// transaction with the company's row locked, and splitting it across
/// repository calls would leave the check and the spend in different
/// transactions - which is exactly how two concurrent unlocks both read the
/// same balance and both spend it.
/// </summary>
public interface IUnlockService
{
    Task<UnlockResult> UnlockAsync(
        long companyId, long jobSeekerId, long actorUserId, CancellationToken ct = default);
}
