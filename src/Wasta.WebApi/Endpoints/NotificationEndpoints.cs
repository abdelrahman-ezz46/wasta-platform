using System.Security.Claims;
using Wasta.Application.Common;
using Wasta.Application.Features.Notifications;
using Wasta.WebApi.Auth;

namespace Wasta.WebApi.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        // Any signed-in actor: seekers, companies and admins all receive these,
        // so there is no role policy here - only the ownership check inside.
        var group = app.MapGroup("/api/notifications")
            .WithTags("Notifications")
            .RequireAuthorization();

        group.MapGet("/", async (
            bool? unreadOnly, int? page, int? pageSize, ClaimsPrincipal user,
            ListNotificationsHandler handler, CancellationToken ct) =>
        {
            var userId = user.UserId();
            return userId is null
                ? Results.Unauthorized()
                : Results.Ok(await handler.HandleAsync(
                    userId.Value, unreadOnly ?? false, new PageRequest(page, pageSize), ct));
        })
        .WithSummary("The signed-in user's notifications, newest first.")
        .Produces<PagedResult<NotificationView>>();

        group.MapGet("/unread-count", async (
            ClaimsPrincipal user, UnreadCountHandler handler, CancellationToken ct) =>
        {
            var userId = user.UserId();
            return userId is null
                ? Results.Unauthorized()
                : Results.Ok(new { unread = await handler.HandleAsync(userId.Value, ct) });
        })
        .WithSummary("Unread count, for the bell badge.");

        group.MapPost("/{notificationId:long}/read", async (
            long notificationId, ClaimsPrincipal user,
            MarkNotificationReadHandler handler, CancellationToken ct) =>
        {
            var userId = user.UserId();
            return userId is null
                ? Results.Unauthorized()
                : ProblemMapping.ToResponse(await handler.HandleAsync(notificationId, userId.Value, ct));
        })
        .WithSummary("Mark one notification read. Someone else's reports 404.")
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/read-all", async (
            ClaimsPrincipal user, MarkAllNotificationsReadHandler handler, CancellationToken ct) =>
        {
            var userId = user.UserId();
            return userId is null
                ? Results.Unauthorized()
                : Results.Ok(new { marked = await handler.HandleAsync(userId.Value, ct) });
        })
        .WithSummary("Mark everything read.");

        return app;
    }
}
