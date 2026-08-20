using Wasta.Domain.Common;
using Wasta.Domain.Localization;

namespace Wasta.Domain.Identity;

/// <summary>
/// The authentication anchor. Job seekers and companies each own exactly one of
/// these; the profile tables hang off it. Keeping credentials here rather than
/// on the seeker or company row means the login path touches one small table
/// and the two actor types cannot drift apart.
/// </summary>
public class UserAccount : Entity<long>, ICreatedAt
{
    private UserAccount() { }

    public UserAccount(string email, string passwordHash, UserRole role)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainException("user.email_required", "An email address is required.");
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("user.password_required", "A password hash is required.");
        }

        // Stored lower-cased so a plain unique index is case-insensitive in
        // effect; Bob@x.com and bob@x.com are the same account.
        Email = email.Trim().ToLowerInvariant();
        PasswordHash = passwordHash;
        Role = role;
        Status = UserStatus.Active;
        Language = Languages.Default;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public string Email { get; private set; } = null!;

    public string PasswordHash { get; private set; } = null!;

    public UserRole Role { get; private set; }

    public UserStatus Status { get; private set; }

    /// <summary>
    /// What language this person reads. Server-rendered prose - notifications
    /// above all - uses it, so a preference set in the app follows the user into
    /// their inbox rather than stopping at the screen.
    /// </summary>
    public Language Language { get; private set; }

    public DateTimeOffset? EmailVerifiedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Set on erasure. The row survives so audit and ledger rows keep their foreign keys.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsEmailVerified => EmailVerifiedAt is not null;

    public bool CanSignIn => Status == UserStatus.Active && DeletedAt is null;

    public void MarkEmailVerified(DateTimeOffset now)
    {
        EmailVerifiedAt ??= now;
        UpdatedAt = now;
    }

    public void ChangePassword(string newHash, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(newHash))
        {
            throw new DomainException("user.password_required", "A password hash is required.");
        }

        PasswordHash = newHash;
        UpdatedAt = now;
    }

    public void SetLanguage(Language language, DateTimeOffset now)
    {
        Language = language;
        UpdatedAt = now;
    }

    public void Suspend(DateTimeOffset now)
    {
        Status = UserStatus.Suspended;
        UpdatedAt = now;
    }

    /// <summary>PDPL erasure: scrub identifying data, keep the row for referential integrity.</summary>
    public void SoftDelete(DateTimeOffset now)
    {
        Status = UserStatus.Deleted;
        Email = $"deleted+{Id}@wasta.invalid";
        PasswordHash = string.Empty;
        DeletedAt = now;
        UpdatedAt = now;
    }
}
