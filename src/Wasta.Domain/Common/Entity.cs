namespace Wasta.Domain.Common;

/// <summary>Base for entities with a database-generated surrogate key.</summary>
public abstract class Entity<TKey>
{
    public TKey Id { get; protected set; } = default!;
}

/// <summary>Marks rows that carry their own creation instant.</summary>
public interface ICreatedAt
{
    DateTimeOffset CreatedAt { get; }
}
