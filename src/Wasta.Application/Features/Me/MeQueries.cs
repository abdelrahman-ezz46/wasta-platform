namespace Wasta.Application.Features.Me;

public sealed record SeekerSummary(
    long SeekerId,
    string FullName,
    string Email,
    int? TrackId,
    short ProfileStrength,
    bool VisibleToCompanies);

public sealed record CompanySummary(
    long CompanyId,
    string Name,
    string Email,
    string VerificationState,
    bool IsVerified);

public sealed record CreditBalance(long CompanyId, int Balance);

/// <summary>
/// Read models for the signed-in actor. Separate from the write-side
/// repositories: these shapes exist to be serialised, not to be mutated.
/// </summary>
public interface IMeQueries
{
    Task<SeekerSummary?> GetSeekerAsync(long seekerId, CancellationToken ct = default);

    Task<CompanySummary?> GetCompanyAsync(long companyId, CancellationToken ct = default);

    /// <summary>Summed from the ledger, never read off a counter column.</summary>
    Task<CreditBalance> GetCreditBalanceAsync(long companyId, CancellationToken ct = default);
}
