using Wasta.Domain.Common;

namespace Wasta.Domain.Credits;

public enum CreditReason
{
    TrialGrant = 1,
    TopUp = 2,
    Unlock = 3,
    Refund = 4,
    Adjustment = 5,
}

/// <summary>
/// Append-only. Balance is the sum of deltas; BalanceAfter is carried for cheap
/// reads and reconciliation, never treated as the source of truth. A bare
/// counter column was rejected: it leaves credit disputes unanswerable and lets
/// two concurrent unlocks both read the same balance and both spend it.
/// </summary>
public class CreditLedgerEntry : Entity<long>, ICreatedAt
{
    private CreditLedgerEntry() { }

    private CreditLedgerEntry(long companyId, int delta, CreditReason reason, int balanceAfter, long? actorUserId, string? note, DateTimeOffset now)
    {
        CompanyId = companyId;
        Delta = delta;
        Reason = reason;
        BalanceAfter = balanceAfter;
        ActorUserId = actorUserId;
        Note = note;
        CreatedAt = now;
    }

    public long CompanyId { get; private set; }

    /// <summary>Signed. Never zero - a no-op entry is a bug, not a record.</summary>
    public int Delta { get; private set; }

    public CreditReason Reason { get; private set; }

    public int BalanceAfter { get; private set; }

    public long? ActorUserId { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static CreditLedgerEntry Credit(
        long companyId, int amount, CreditReason reason, int currentBalance, long? actorUserId, string? note, DateTimeOffset now)
    {
        if (amount <= 0)
        {
            throw new DomainException("credits.amount_invalid", "A credit must be a positive number.");
        }

        return new CreditLedgerEntry(companyId, amount, reason, currentBalance + amount, actorUserId, note, now);
    }

    public static CreditLedgerEntry Debit(
        long companyId, int amount, CreditReason reason, int currentBalance, long? actorUserId, string? note, DateTimeOffset now)
    {
        if (amount <= 0)
        {
            throw new DomainException("credits.amount_invalid", "A debit must be a positive number.");
        }

        if (currentBalance < amount)
        {
            throw new DomainException("credits.insufficient", "Not enough credits for this action.");
        }

        return new CreditLedgerEntry(companyId, -amount, reason, currentBalance - amount, actorUserId, note, now);
    }
}

public enum TopUpState
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
}

/// <summary>
/// Bank transfer only. The company asks, an admin confirms the money arrived,
/// then issues credits. No card ever touches this system.
/// </summary>
public class CreditTopUpRequest : Entity<long>, ICreatedAt
{
    private CreditTopUpRequest() { }

    public CreditTopUpRequest(long companyId, int creditsRequested, int paymentMethodId, decimal? amount, string? currency, DateTimeOffset now)
    {
        if (creditsRequested <= 0)
        {
            throw new DomainException("topup.credits_invalid", "Requested credits must be greater than zero.");
        }

        CompanyId = companyId;
        CreditsRequested = creditsRequested;
        PaymentMethodId = paymentMethodId;
        Amount = amount;
        Currency = currency?.ToUpperInvariant();
        State = TopUpState.Pending;
        CreatedAt = now;
    }

    public long CompanyId { get; private set; }
    public int CreditsRequested { get; private set; }
    public int PaymentMethodId { get; private set; }
    public decimal? Amount { get; private set; }
    public string? Currency { get; private set; }
    public TopUpState State { get; private set; }
    public long? ReviewedByUserId { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public long? LedgerEntryId { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Approve(long adminUserId, long ledgerEntryId, DateTimeOffset now)
    {
        if (State != TopUpState.Pending)
        {
            throw new DomainException("topup.not_pending", "This request has already been reviewed.");
        }

        State = TopUpState.Approved;
        ReviewedByUserId = adminUserId;
        ReviewedAt = now;
        LedgerEntryId = ledgerEntryId;
    }

    public void Reject(long adminUserId, string? note, DateTimeOffset now)
    {
        if (State != TopUpState.Pending)
        {
            throw new DomainException("topup.not_pending", "This request has already been reviewed.");
        }

        State = TopUpState.Rejected;
        ReviewedByUserId = adminUserId;
        ReviewedAt = now;
        Note = note;
    }
}

/// <summary>
/// A company revealing one candidate's identity. Unique per pair, so a retry, a
/// double-click, or a second visit never charges twice.
/// </summary>
public class ProfileUnlock : Entity<long>, ICreatedAt
{
    private ProfileUnlock() { }

    public ProfileUnlock(long companyId, long jobSeekerId, long ledgerEntryId, DateTimeOffset now)
    {
        CompanyId = companyId;
        JobSeekerId = jobSeekerId;
        LedgerEntryId = ledgerEntryId;
        CreatedAt = now;
    }

    public long CompanyId { get; private set; }
    public long JobSeekerId { get; private set; }
    public long LedgerEntryId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
