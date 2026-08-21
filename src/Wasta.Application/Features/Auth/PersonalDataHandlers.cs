using Wasta.Application.Abstractions;
using Wasta.Application.Common;
using Wasta.Application.Features.Files;

namespace Wasta.Application.Features.Auth;

public sealed record PersonalDataExport(
    DateTimeOffset GeneratedAt,
    object Account,
    object? Seeker,
    object? Company,
    IReadOnlyList<object> Attempts,
    IReadOnlyList<object> Applications,
    IReadOnlyList<object> Unlocks,
    IReadOnlyList<object> Notifications);

/// <summary>
/// The two PDPL rights the platform has to be able to honour on request:
/// access, and erasure.
/// </summary>
public interface IPersonalDataQueries
{
    Task<PersonalDataExport?> ExportAsync(long userId, CancellationToken ct = default);
}

public interface IPersonalDataEraser
{
    /// <summary>Returns the CV key to delete from storage, if there was one.</summary>
    Task<string?> EraseAsync(long userId, DateTimeOffset now, CancellationToken ct = default);
}

public class ExportPersonalDataHandler(IPersonalDataQueries queries)
{
    public async Task<Result<PersonalDataExport>> HandleAsync(long userId, CancellationToken ct = default)
    {
        var export = await queries.ExportAsync(userId, ct);

        return export is null
            ? Result.Failure<PersonalDataExport>("user.not_found", "That account does not exist.")
            : Result.Success(export);
    }
}

public class DeleteAccountHandler(
    IUserAccountRepository users,
    IAccountTokenRepository tokens,
    IPersonalDataEraser eraser,
    IFileStore files,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(long userId, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId, ct);
        if (user is null || user.DeletedAt is not null)
        {
            return Result.Failure("user.not_found", "That account does not exist.");
        }

        var now = clock.UtcNow;

        // Scrub rather than delete rows. Credit ledger entries and unlock
        // records are financial history a company is entitled to keep, and
        // hard-deleting the person would either break those foreign keys or
        // erase the other party's records along with theirs. Everything that
        // identifies the person goes; the shape of what happened stays.
        var cvKey = await eraser.EraseAsync(userId, now, ct);

        await tokens.RevokeAllRefreshTokensAsync(userId, now, ct);
        user.SoftDelete(now);

        audit.Write(userId, "account.erased", "user_account", userId.ToString(), null, now);

        await unitOfWork.SaveChangesAsync(ct);

        // After the commit: a file deleted before the transaction succeeded
        // would be gone even if the erasure rolled back.
        if (!string.IsNullOrEmpty(cvKey))
        {
            await files.DeleteAsync(cvKey, ct);
        }

        return Result.Success();
    }
}
