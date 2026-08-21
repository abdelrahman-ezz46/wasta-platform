using Wasta.Domain.Audit;

namespace Wasta.Application.Features.Notifications;

/// <summary>
/// Stable machine-readable kinds. Clients switch on these to render, and the
/// dispatcher uses them to pick a template, so renaming one is a breaking
/// change to both.
/// </summary>
public static class NotificationKinds
{
    public const string ResultsReady = "assessment.results_ready";
    public const string ProfileUnlocked = "profile.unlocked";
    public const string ApplicationStatusChanged = "application.status_changed";
    public const string CompanyApproved = "company.approved";
    public const string CompanyRejected = "company.rejected";
    public const string CreditsIssued = "credits.issued";

    // Sent inline rather than queued - see AccountEmails for why.
    public const string EmailVerification = "account.email_verification";
    public const string PasswordReset = "account.password_reset";
}

public sealed record NotificationView(
    long NotificationId,
    string Kind,
    string Payload,
    bool IsRead,
    DateTimeOffset CreatedAt);

public sealed record OutboundMessage(
    NotificationChannel Channel,
    string Recipient,
    string Subject,
    string Body);

/// <summary>
/// Queues a notification. Deliberately does not save: the caller owns the
/// transaction, so the notification lands with the thing that caused it or not
/// at all. A notification that survives a rolled-back unlock is a lie.
/// </summary>
public interface INotificationService
{
    void Queue(
        long userId,
        string kind,
        object payload,
        NotificationChannel channel = NotificationChannel.Email);
}

/// <summary>
/// Actually delivers a message. Implementations talk to an email or SMS
/// provider; the one in this repo writes to the log.
/// </summary>
public interface INotificationSender
{
    Task SendAsync(OutboundMessage message, CancellationToken ct = default);

    /// <summary>False for the logging stand-in, which the host warns about at startup.</summary>
    bool IsRealSender { get; }
}

/// <summary>
/// Maps an actor to the account that receives their mail. Handlers work in
/// seeker and company ids; notifications are addressed to user accounts.
/// </summary>
public interface INotificationRecipients
{
    Task<long?> UserIdForSeekerAsync(long seekerId, CancellationToken ct = default);

    Task<long?> UserIdForCompanyAsync(long companyId, CancellationToken ct = default);

    Task<string?> CompanyNameAsync(long companyId, CancellationToken ct = default);
}

public interface INotificationQueries
{
    Task<Common.PagedResult<NotificationView>> ListAsync(
        long userId, bool unreadOnly, Common.PageRequest page, CancellationToken ct = default);

    Task<int> UnreadCountAsync(long userId, CancellationToken ct = default);
}

public interface INotificationRepository
{
    Task<Domain.Audit.Notification?> FindAsync(long notificationId, CancellationToken ct = default);

    Task<int> MarkAllReadAsync(long userId, DateTimeOffset now, CancellationToken ct = default);
}
