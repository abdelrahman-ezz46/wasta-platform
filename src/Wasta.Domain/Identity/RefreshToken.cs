using Wasta.Domain.Common;

namespace Wasta.Domain.Identity;

/// <summary>
/// One issued refresh token. Rotation replaces a token on every use and records
/// the successor, so presenting an already-rotated token is detectable: that
/// means the token leaked, and the whole family is revoked rather than the one
/// token, because an attacker holding any link in the chain is still a breach.
/// </summary>
public class RefreshToken : Entity<long>, ICreatedAt
{
    private RefreshToken() { }

    public RefreshToken(long userId, string tokenHash, DateTimeOffset expiresAt, DateTimeOffset now, long familyId = 0)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        CreatedAt = now;
        FamilyId = familyId;
    }

    public long UserId { get; private set; }

    /// <summary>Hash only. A stolen database must not yield usable refresh tokens.</summary>
    public string TokenHash { get; private set; } = null!;

    /// <summary>Groups a rotation chain. Reuse revokes every token sharing it.</summary>
    public long FamilyId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public bool IsActive(DateTimeOffset now) =>
        RevokedAt is null && UsedAt is null && ExpiresAt > now;

    public void MarkUsed(DateTimeOffset now) => UsedAt = now;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    public void AssignFamily(long familyId) => FamilyId = familyId;
}
