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

    /// <summary>
    /// A high-entropy opaque token for links sent by email. Same shape as a
    /// refresh token: the raw value goes out once, only the hash is kept.
    /// </summary>
    (string Raw, string Hash) CreateOpaqueToken();

    string HashOpaqueToken(string raw);

    TimeSpan AccessTokenLifetime { get; }

    TimeSpan RefreshTokenLifetime { get; }
}

/// <summary>Commits a unit of work. Handlers decide the boundary; Infrastructure owns the mechanism.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Runs the operation inside one transaction, rolling back on any exception.
    /// Lets a handler say "these writes land together" without knowing what a
    /// transaction is - approving a company and granting its trial credits must
    /// not be separable, or an approval could land with no credits behind it.
    /// </summary>
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default);
}

/// <summary>
/// Records who did what. Queued like a notification rather than saved, so an
/// audit row commits with the action it describes - an audit log that survives
/// a rolled-back transaction records something that never happened.
/// </summary>
public interface IAuditWriter
{
    void Write(
        long? actorUserId,
        string action,
        string entityType,
        string entityId,
        object? detail,
        DateTimeOffset now);
}

/// <summary>
/// Kicks off Career Coach plan generation after an assessment is scored.
///
/// An interface here rather than a direct call so the Application layer keeps
/// no dependency on the AI modules. It is also fire-and-forget by contract: the
/// coach is an optional add-on to the results page, and a student must never
/// wait on it or lose their score because it failed.
/// </summary>
public interface ICoachPlanTrigger
{
    Task EnqueueAsync(long seekerId, long attemptId, CancellationToken ct = default);
}

/// <summary>Used when the AI modules are not wired in. Does nothing, quietly and on purpose.</summary>
public sealed class NoCoachPlanTrigger : ICoachPlanTrigger
{
    public Task EnqueueAsync(long seekerId, long attemptId, CancellationToken ct = default) =>
        Task.CompletedTask;
}

/// <summary>
/// The narrowest possible logging seam. Application declares this rather than
/// taking Microsoft.Extensions.Logging so the layer keeps its dependency list
/// honest; Infrastructure adapts it to the real logger.
/// </summary>
public interface ILoggerAdapter
{
    void Warn(string template, params object?[] args);
}
