using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wasta.Application.Abstractions;
using Wasta.Domain.Audit;
using Wasta.Domain.Identity;

namespace Wasta.Infrastructure.Persistence.Repositories;

public sealed class AccountTokenRepository(WastaDbContext db) : IAccountTokenRepository
{
    public Task<AccountToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default) =>
        db.AccountTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task InvalidateOutstandingAsync(
        long userId, AccountTokenPurpose purpose, DateTimeOffset now, CancellationToken ct = default)
    {
        var outstanding = await db.AccountTokens
            .Where(t => t.UserId == userId
                        && t.Purpose == purpose
                        && t.UsedAt == null
                        && t.InvalidatedAt == null)
            .ToListAsync(ct);

        foreach (var token in outstanding)
        {
            token.Invalidate(now);
        }
    }

    public void Add(AccountToken token) => db.AccountTokens.Add(token);

    public async Task RevokeAllRefreshTokensAsync(
        long userId, DateTimeOffset now, CancellationToken ct = default)
    {
        var active = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in active)
        {
            token.Revoke(now);
        }
    }
}

public sealed class AuditWriter(WastaDbContext db) : IAuditWriter
{
    public void Write(
        long? actorUserId,
        string action,
        string entityType,
        string entityId,
        object? detail,
        DateTimeOffset now)
    {
        // Added, not saved: the audit row commits with the action it describes.
        db.AuditLog.Add(new AuditLogEntry(
            actorUserId,
            action,
            entityType,
            entityId,
            detail is null ? null : JsonSerializer.Serialize(detail),
            now));
    }
}
