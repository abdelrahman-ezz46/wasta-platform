using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wasta.Application.Features.Notifications;
using Wasta.Domain.Audit;
using Wasta.Infrastructure.Persistence;

namespace Wasta.Infrastructure.Notifications;

public sealed class NotificationDispatcherOptions
{
    public const string SectionName = "Notifications";

    public int PollSeconds { get; set; } = 15;

    public int BatchSize { get; set; } = 50;

    /// <summary>Off by default in tests, which drive dispatch directly instead of racing a timer.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Drains pending notifications in the background.
///
/// Separating the write from the send is the point: a request commits the
/// notification with the change that caused it and returns, and delivery
/// happens afterwards. Sending inline would put an SMTP round trip inside a
/// user's request and lose the message whenever that round trip failed.
/// </summary>
public sealed class NotificationDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationDispatcherOptions> options,
    ILogger<NotificationDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Notification dispatcher is disabled by configuration.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.PollSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await DispatchBatchAsync(scope.ServiceProvider, options.Value.BatchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The loop must outlive a bad batch. A dispatcher that dies on
                // one malformed row stops every notification after it.
                logger.LogError(ex, "Notification dispatch batch failed.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Exposed so tests can drive one batch deterministically rather than
    /// sleeping until the timer happens to fire.
    /// </summary>
    public static async Task<int> DispatchBatchAsync(
        IServiceProvider services, int batchSize, CancellationToken ct)
    {
        var db = services.GetRequiredService<WastaDbContext>();
        var sender = services.GetRequiredService<INotificationSender>();
        var clock = services.GetRequiredService<Wasta.Application.Abstractions.IClock>();
        var logger = services.GetRequiredService<ILogger<NotificationDispatcher>>();

        var now = clock.UtcNow;

        var pending = await db.Notifications
            .Where(n => n.DeliveryState == DeliveryState.Pending)
            .OrderBy(n => n.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);

        var sent = 0;

        foreach (var notification in pending)
        {
            // Backoff is derived from the row's age and attempt count, so a
            // failing provider is retried more slowly without any scheduler
            // state to keep.
            if (!notification.IsDueForDispatch(now))
            {
                continue;
            }

            var recipient = await db.UserAccounts
                .Where(u => u.Id == notification.UserId)
                .Select(u => new { u.Email, u.DeletedAt, u.Language })
                .FirstOrDefaultAsync(ct);

            if (recipient is null || recipient.DeletedAt is not null)
            {
                // The account went away, or was erased. Nothing to deliver to,
                // and retrying forever would keep a deleted address in rotation.
                notification.MarkAttemptFailed("Recipient no longer available.", now);
                continue;
            }

            // The recipient's own language, not the language of whoever caused
            // this. A company acting in English must not get an Arabic-reading
            // student emailed in English.
            var (subject, body) = NotificationRenderer.Render(
                notification.Kind, notification.Payload, recipient.Language);

            try
            {
                await sender.SendAsync(
                    new OutboundMessage(notification.Channel, recipient.Email, subject, body), ct);

                notification.MarkSent(now);
                sent++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex, "Delivery failed for notification {Id}, attempt {Attempt}.",
                    notification.Id, notification.Attempts + 1);

                notification.MarkAttemptFailed(ex.Message, now);
            }
        }

        await db.SaveChangesAsync(ct);
        return sent;
    }
}
