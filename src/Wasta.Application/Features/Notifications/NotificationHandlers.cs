using Wasta.Application.Abstractions;
using Wasta.Application.Common;

namespace Wasta.Application.Features.Notifications;

public class ListNotificationsHandler(INotificationQueries queries)
{
    public Task<PagedResult<NotificationView>> HandleAsync(
        long userId, bool unreadOnly, PageRequest page, CancellationToken ct = default) =>
        queries.ListAsync(userId, unreadOnly, page, ct);
}

public class UnreadCountHandler(INotificationQueries queries)
{
    public Task<int> HandleAsync(long userId, CancellationToken ct = default) =>
        queries.UnreadCountAsync(userId, ct);
}

public class MarkNotificationReadHandler(
    INotificationRepository notifications,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> HandleAsync(long notificationId, long userId, CancellationToken ct = default)
    {
        var notification = await notifications.FindAsync(notificationId, ct);

        // Someone else's notification reports "not found", like every other
        // ownership failure in this API.
        if (notification is null || notification.UserId != userId)
        {
            return Result.Failure("notification.not_found", "That notification does not exist.");
        }

        notification.MarkRead(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}

public class MarkAllNotificationsReadHandler(
    INotificationRepository notifications,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<int> HandleAsync(long userId, CancellationToken ct = default)
    {
        var count = await notifications.MarkAllReadAsync(userId, clock.UtcNow, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return count;
    }
}
