using Wasta.Domain.Identity;

namespace Wasta.Application.Abstractions;

/// <summary>
/// Time as a dependency. Tests that turn on a 30-day retake window or a token
/// expiry cannot be written against DateTimeOffset.UtcNow.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string hash);
}

/// <summary>A freshly issued token pair plus the moment the access token dies.</summary>
public sealed record TokenPair(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt);

public interface ITokenService
{
    string CreateAccessToken(UserAccount user, long? seekerId, long? companyId);

    /// <summary>Returns the raw token for the client and the hash to persist. The raw value is never stored.</summary>
    (string Raw, string Hash) CreateRefreshToken();

    string HashRefreshToken(string raw);

    TimeSpan AccessTokenLifetime { get; }

    TimeSpan RefreshTokenLifetime { get; }
}

/// <summary>Commits a unit of work. Handlers decide the boundary; Infrastructure owns the mechanism.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
