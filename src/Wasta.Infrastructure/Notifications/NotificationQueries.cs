using Microsoft.EntityFrameworkCore;
using Wasta.Application.Common;
using Wasta.Application.Features.Notifications;
using Wasta.Domain.Audit;
using Wasta.Infrastructure.Persistence;

namespace Wasta.Infrastructure.Notifications;

public sealed class NotificationRecipients(WastaDbContext db) : INotificationRecipients
{
    public async Task<long?> UserIdForSeekerAsync(long seekerId, CancellationToken ct = default) =>
        await db.JobSeekers.AsNoTracking()
            .Where(s => s.Id == seekerId).Select(s => (long?)s.UserId).FirstOrDefaultAsync(ct);

    public async Task<long?> UserIdForCompanyAsync(long companyId, CancellationToken ct = default) =>
        await db.Companies.AsNoTracking()
            .Where(c => c.Id == companyId).Select(c => (long?)c.UserId).FirstOrDefaultAsync(ct);

    public async Task<string?> CompanyNameAsync(long companyId, CancellationToken ct = default) =>
        await db.Companies.AsNoTracking()
            .Where(c => c.Id == companyId).Select(c => c.Name).FirstOrDefaultAsync(ct);
}

public sealed class NotificationQueries(WastaDbContext db) : INotificationQueries
{
    public async Task<PagedResult<NotificationView>> ListAsync(
        long userId, bool unreadOnly, PageRequest page, CancellationToken ct = default)
    {
        var query = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);

        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(n => new NotificationView(n.Id, n.Kind, n.Payload, n.ReadAt != null, n.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<NotificationView>(items, page.Page, page.PageSize, total);
    }

    public Task<int> UnreadCountAsync(long userId, CancellationToken ct = default) =>
        db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null, ct);
}

public sealed class NotificationRepository(WastaDbContext db) : INotificationRepository
{
    public Task<Notification?> FindAsync(long notificationId, CancellationToken ct = default) =>
        db.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId, ct);

    public async Task<int> MarkAllReadAsync(long userId, DateTimeOffset now, CancellationToken ct = default)
    {
        var unread = await db.Notifications.Where(n => n.UserId == userId && n.ReadAt == null).ToListAsync(ct);

        foreach (var notification in unread)
        {
            notification.MarkRead(now);
        }

        return unread.Count;
    }
}
