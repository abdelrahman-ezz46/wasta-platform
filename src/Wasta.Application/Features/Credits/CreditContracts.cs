namespace Wasta.Application.Features.Credits;

public sealed record LedgerEntryView(
    long EntryId,
    int Delta,
    string Reason,
    int BalanceAfter,
    string? Note,
    DateTimeOffset CreatedAt);

public sealed record TopUpRequestView(
    long RequestId,
    long CompanyId,
    string CompanyName,
    int CreditsRequested,
    decimal? Amount,
    string? Currency,
    string State,
    string? Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReviewedAt);

public sealed record RequestTopUpCommand(
    long CompanyId,
    int CreditsRequested,
    int PaymentMethodId,
    decimal? Amount,
    string? Currency);

public sealed record ReviewTopUpCommand(
    long RequestId,
    long AdminUserId,
    bool Approve,
    string? Note);

public sealed record ApproveCompanyCommand(long CompanyId, long AdminUserId);

public sealed record RejectCompanyCommand(long CompanyId, long AdminUserId, string Note);

public sealed record PendingCompanyView(
    long CompanyId,
    string Name,
    string Email,
    string? Website,
    int DocumentCount,
    DateTimeOffset CreatedAt);
