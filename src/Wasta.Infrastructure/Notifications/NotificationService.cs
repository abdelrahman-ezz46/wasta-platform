using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wasta.Application.Features.Notifications;
using Wasta.Domain.Audit;
using Wasta.Infrastructure.Persistence;

namespace Wasta.Infrastructure.Notifications;

public sealed class NotificationService(WastaDbContext db, Wasta.Application.Abstractions.IClock clock)
    : INotificationService
{
    public void Queue(
        long userId, string kind, object payload, NotificationChannel channel = NotificationChannel.Email)
    {
        // Added, not saved. The caller's SaveChanges commits this alongside
        // whatever caused it, so the two cannot come apart.
        db.Notifications.Add(new Notification(
            userId, kind, JsonSerializer.Serialize(payload), clock.UtcNow, channel));
    }
}

/// <summary>
/// Writes messages to the log instead of sending them.
///
/// Present so the pipeline is complete and swappable, not so the box is ticked.
/// Nothing here reaches a real inbox: password resets, verification links and
/// unlock alerts all stop at the log until a provider is wired in behind
/// INotificationSender.
/// </summary>
public sealed class LoggingNotificationSenderOptions
{
    public const string SectionName = "Notifications";

    /// <summary>
    /// Writes message bodies to the log as well as subjects.
    ///
    /// Off by default and must stay off outside a developer's machine:
    /// verification and reset links are bearer credentials, and logging one
    /// puts it wherever the logs go. It exists because without it there is no
    /// way to complete those flows locally with no mail provider wired up.
    /// </summary>
    public bool LogBodies { get; set; }
}

public sealed class LoggingNotificationSender(
    ILogger<LoggingNotificationSender> logger,
    IOptions<LoggingNotificationSenderOptions> options) : INotificationSender
{
    public bool IsRealSender => false;

    public Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        // The recipient is logged because this is a development stand-in and the
        // log is where the message can be read. A real sender must not log
        // addresses - that would put personal data in the log stream.
        if (options.Value.LogBodies)
        {
            logger.LogInformation(
                "[{Channel}] to {Recipient}: {Subject}\n{Body}",
                message.Channel, message.Recipient, message.Subject, message.Body);
        }
        else
        {
            logger.LogInformation(
                "[{Channel}] to {Recipient}: {Subject}", message.Channel, message.Recipient, message.Subject);
        }

        return Task.CompletedTask;
    }
}

public sealed class NotificationSenderStartupCheck(
    INotificationSender sender,
    IOptions<LoggingNotificationSenderOptions> bodyLogging,
    ILogger<NotificationSenderStartupCheck> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (sender.IsRealSender)
        {
            logger.LogInformation("Notifications are being sent by {Sender}.", sender.GetType().Name);
        }
        else
        {
            logger.LogWarning(
                "Notifications are NOT being delivered. {Sender} writes to the log instead of sending. "
                + "Password resets, verification and unlock alerts will not reach anyone until a real "
                + "provider is wired in.",
                sender.GetType().Name);
        }

        if (bodyLogging.Value.LogBodies)
        {
            logger.LogWarning(
                "Notifications:LogBodies is on. Verification and password-reset links are being written "
                + "to the log in full. This is for local development only - turn it off anywhere that "
                + "keeps logs.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
