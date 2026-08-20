using Wasta.Domain.Common;

namespace Wasta.Domain.Audit;

/// <summary>Where a notification is meant to end up, beyond the in-app list.</summary>
public enum NotificationChannel
{
    /// <summary>In-app only. The row itself is the notification; nothing is dispatched.</summary>
    InApp = 1,

    Email = 2,
    Sms = 3,
}

public enum DeliveryState
{
    Pending = 1,
    Sent = 2,

    /// <summary>Retries exhausted. Kept, so a failure is visible rather than silent.</summary>
    Failed = 3,

    /// <summary>Nothing to dispatch, because the channel is in-app.</summary>
    NotApplicable = 4,
}

/// <summary>
/// One notification, and its outbound delivery if it has one.
///
/// Written in the same transaction as whatever caused it, then picked up by a
/// background dispatcher. Sending inline would either block the request on an
/// SMTP round trip or lose the message when that round trip fails.
/// </summary>
public class Notification : Entity<long>, ICreatedAt
{
    /// <summary>Give up after this many attempts and leave the row Failed.</summary>
    public const int MaxAttempts = 5;

    private Notification() { }

    public Notification(
        long userId,
        string kind,
        string payload,
        DateTimeOffset now,
        NotificationChannel channel = NotificationChannel.InApp)
    {
        UserId = userId;
        Kind = kind;
        Payload = payload;
        Channel = channel;
        DeliveryState = channel == NotificationChannel.InApp
            ? DeliveryState.NotApplicable
            : DeliveryState.Pending;
        CreatedAt = now;
    }

    public long UserId { get; private set; }

    /// <summary>Stable machine-readable type. Clients switch on this to render.</summary>
    public string Kind { get; private set; } = null!;

    /// <summary>jsonb. Only the recipient's own data - this is read back to them.</summary>
    public string Payload { get; private set; } = null!;

    public NotificationChannel Channel { get; private set; }

    public DeliveryState DeliveryState { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? DispatchedAt { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;

    public void MarkSent(DateTimeOffset now)
    {
        DeliveryState = DeliveryState.Sent;
        DispatchedAt = now;
        Attempts++;
        LastError = null;
    }

    /// <summary>
    /// Records a failed attempt. Stays Pending until the cap, so a transient
    /// outage is retried rather than dropped on the first refusal.
    /// </summary>
    public void MarkAttemptFailed(string error, DateTimeOffset now)
    {
        Attempts++;

        // Truncated: a provider returning a wall of HTML should not become a
        // wall of HTML in the database.
        LastError = error.Length > 500 ? error[..500] : error;

        if (Attempts >= MaxAttempts)
        {
            DeliveryState = DeliveryState.Failed;
            DispatchedAt = now;
        }
    }

    /// <summary>
    /// The first attempt goes immediately; only a failure earns a delay.
    /// Backoff is measured from the row's creation and widens with the attempt
    /// count, so a failing provider is retried more slowly without any
    /// scheduler state to keep.
    /// </summary>
    public bool IsDueForDispatch(DateTimeOffset now) =>
        DeliveryState == DeliveryState.Pending
        && (Attempts == 0 || now >= CreatedAt.AddSeconds(Math.Pow(2, Attempts) * 10));
}
