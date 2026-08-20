using Wasta.Application.Abstractions;
using Wasta.Application.Common;

namespace Wasta.Application.Features.TalentPool;

public class BrowseTalentPoolHandler(ITalentPoolQueries queries)
{
    public Task<PagedResult<TalentPoolCandidate>> HandleAsync(
        BrowseTalentPoolQuery query, CancellationToken ct = default) =>
        queries.BrowseAsync(query, ct);
}

public class GetCandidateHandler(ITalentPoolQueries queries)
{
    public async Task<Result<CandidateDetail>> HandleAsync(
        long companyId, long jobSeekerId, CancellationToken ct = default)
    {
        var candidate = await queries.GetCandidateAsync(companyId, jobSeekerId, ct);

        // A seeker who has opted out reports the same "not found" as one who
        // does not exist. Distinguishing them would tell a company that a
        // particular person is on the platform but hiding.
        return candidate is null
            ? Result.Failure<CandidateDetail>("candidate.not_found", "That candidate is not available.")
            : Result.Success(candidate);
    }
}

public class UnlockCandidateHandler(IUnlockService unlocks)
{
    public async Task<Result<UnlockResult>> HandleAsync(
        long companyId, long jobSeekerId, long actorUserId, CancellationToken ct = default)
    {
        var result = await unlocks.UnlockAsync(companyId, jobSeekerId, actorUserId, ct);

        return result.Outcome switch
        {
            // Already held is a success, not an error: a retry, a double-click,
            // or simply revisiting the profile must not read as a failure, and
            // must never charge twice.
            UnlockOutcome.Unlocked or UnlockOutcome.AlreadyUnlocked => Result.Success(result),

            UnlockOutcome.InsufficientCredits => Result.Failure<UnlockResult>(
                "credits.insufficient", "Not enough credits to unlock this candidate."),

            _ => Result.Failure<UnlockResult>("candidate.not_found", "That candidate is not available."),
        };
    }
}
