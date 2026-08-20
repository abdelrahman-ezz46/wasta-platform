using Wasta.Application.Common;
using Wasta.Application.Features.Credits;
using Wasta.Application.Features.TalentPool;
using Wasta.Domain.Companies;
using Wasta.Domain.Credits;

namespace Wasta.Application.Abstractions;

public interface ITalentPoolQueries
{
    Task<PagedResult<TalentPoolCandidate>> BrowseAsync(
        BrowseTalentPoolQuery query, CancellationToken ct = default);

    /// <summary>
    /// Null when the candidate does not exist or has opted out of the pool, so
    /// an invisible seeker is indistinguishable from one that was never there.
    /// </summary>
    Task<CandidateDetail?> GetCandidateAsync(
        long companyId, long jobSeekerId, CancellationToken ct = default);
}

public interface ICreditQueries
{
    Task<PagedResult<LedgerEntryView>> GetLedgerAsync(
        long companyId, PageRequest page, CancellationToken ct = default);

    Task<PagedResult<TopUpRequestView>> GetTopUpRequestsForCompanyAsync(
        long companyId, PageRequest page, CancellationToken ct = default);

    Task<PagedResult<TopUpRequestView>> GetPendingTopUpRequestsAsync(
        PageRequest page, CancellationToken ct = default);

    Task<PagedResult<PendingCompanyView>> GetPendingCompaniesAsync(
        PageRequest page, CancellationToken ct = default);

    /// <summary>Summed from the ledger. Never read off a carried counter.</summary>
    Task<int> GetBalanceAsync(long companyId, CancellationToken ct = default);
}

public interface ICompanyRepositoryForAdmin
{
    Task<Company?> FindAsync(long companyId, CancellationToken ct = default);

    Task<bool> HasTrialGrantAsync(long companyId, CancellationToken ct = default);
}

public interface ICreditRepository
{
    void AddLedgerEntry(CreditLedgerEntry entry);

    void AddTopUpRequest(CreditTopUpRequest request);

    Task<CreditTopUpRequest?> FindTopUpRequestAsync(long requestId, CancellationToken ct = default);

    Task<bool> PaymentMethodExistsAsync(int paymentMethodId, CancellationToken ct = default);
}
