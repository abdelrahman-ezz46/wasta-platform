using Wasta.Domain.Common;

namespace Wasta.Domain.Identity;

public enum AccountTokenPurpose
{
    EmailVerification = 1,
    PasswordReset = 2,
}

/// <summary>
/// A single-use, time-limited token sent to a person's inbox.
///
/// Only the hash is stored, for the same reason refresh tokens are hashed: a
/// leaked database must not hand an attacker the ability to reset every
/// account's password. The raw value exists once, in the email, and is never
/// recoverable from here.
/// </summary>
public class AccountToken : Entity<long>, ICreatedAt
{
    public static readonly TimeSpan VerificationLifetime = TimeSpan.FromDays(3);

    /// <summary>
    /// Deliberately short. A password reset link is a bearer credential sitting
    /// in an inbox, and inboxes get read by people who should not have them.
    /// </summary>
    public static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromHours(1);

    private AccountToken() { }

    public AccountToken(long userId, AccountTokenPurpose purpose, string tokenHash, DateTimeOffset now)
    {
        UserId = userId;
        Purpose = purpose;
        TokenHash = tokenHash;
        CreatedAt = now;
        ExpiresAt = now.Add(LifetimeFor(purpose));
    }

    public static TimeSpan LifetimeFor(AccountTokenPurpose purpose) => purpose switch
    {
        AccountTokenPurpose.PasswordReset => PasswordResetLifetime,
        _ => VerificationLifetime,
    };

    public long UserId { get; private set; }

    public AccountTokenPurpose Purpose { get; private set; }

    public string TokenHash { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public DateTimeOffset? InvalidatedAt { get; private set; }

    public bool IsUsable(DateTimeOffset now) =>
        UsedAt is null && InvalidatedAt is null && ExpiresAt > now;

    public void MarkUsed(DateTimeOffset now) => UsedAt ??= now;

    /// <summary>
    /// Retired without being used. Issuing a new token invalidates the old one,
    /// so a person who requests two resets cannot leave a spare valid link
    /// sitting in their inbox.
    /// </summary>
    public void Invalidate(DateTimeOffset now) => InvalidatedAt ??= now;
}
