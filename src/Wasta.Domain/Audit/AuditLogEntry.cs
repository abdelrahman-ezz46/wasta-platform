using Wasta.Domain.Common;

namespace Wasta.Domain.Audit;

public class AuditLogEntry : Entity<long>, ICreatedAt
{
    private AuditLogEntry() { }

    public AuditLogEntry(long? actorUserId, string action, string entityType, string entityId, string? detail, DateTimeOffset now)
    {
        ActorUserId = actorUserId;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        Detail = detail;
        CreatedAt = now;
    }

    public long? ActorUserId { get; private set; }
    public string Action { get; private set; } = null!;
    public string EntityType { get; private set; } = null!;
    public string EntityId { get; private set; } = null!;

    /// <summary>jsonb. Never holds credentials or personal data - this table is widely readable.</summary>
    public string? Detail { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

public class Notification : Entity<long>, ICreatedAt
{
    private Notification() { }

    public Notification(long userId, string kind, string payload, DateTimeOffset now)
    {
        UserId = userId;
        Kind = kind;
        Payload = payload;
        CreatedAt = now;
    }

    public long UserId { get; private set; }
    public string Kind { get; private set; } = null!;
    public string Payload { get; private set; } = null!;
    public DateTimeOffset? ReadAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;
}
