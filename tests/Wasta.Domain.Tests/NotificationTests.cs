using Wasta.Domain.Audit;

namespace Wasta.Domain.Tests;

public class NotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    private static Notification Email() =>
        new(userId: 1, kind: "test.kind", payload: "{}", Now, NotificationChannel.Email);

    [Fact]
    public void An_in_app_notification_has_nothing_to_dispatch()
    {
        var notification = new Notification(1, "test.kind", "{}", Now, NotificationChannel.InApp);

        Assert.Equal(DeliveryState.NotApplicable, notification.DeliveryState);
        Assert.False(notification.IsDueForDispatch(Now));
    }

    [Fact]
    public void An_email_notification_is_due_immediately()
    {
        // The first attempt must not wait. Only a failure earns a delay.
        Assert.True(Email().IsDueForDispatch(Now));
    }

    [Fact]
    public void A_failed_attempt_pushes_the_next_one_out()
    {
        var notification = Email();
        notification.MarkAttemptFailed("smtp timeout", Now);

        Assert.False(notification.IsDueForDispatch(Now));
        Assert.False(notification.IsDueForDispatch(Now.AddSeconds(19)));
        Assert.True(notification.IsDueForDispatch(Now.AddSeconds(21)));
    }

    [Fact]
    public void Backoff_widens_with_each_failure()
    {
        var notification = Email();
        notification.MarkAttemptFailed("one", Now);
        notification.MarkAttemptFailed("two", Now.AddSeconds(21));

        // 2^2 * 10 = 40 seconds from creation, against 20 after the first.
        Assert.False(notification.IsDueForDispatch(Now.AddSeconds(39)));
        Assert.True(notification.IsDueForDispatch(Now.AddSeconds(41)));
    }

    [Fact]
    public void It_stays_pending_until_the_attempt_cap()
    {
        var notification = Email();

        for (var i = 1; i < Notification.MaxAttempts; i++)
        {
            notification.MarkAttemptFailed($"attempt {i}", Now);
            Assert.Equal(DeliveryState.Pending, notification.DeliveryState);
        }

        // A transient outage should be retried, not dropped on first refusal.
        notification.MarkAttemptFailed("final", Now);
        Assert.Equal(DeliveryState.Failed, notification.DeliveryState);
        Assert.False(notification.IsDueForDispatch(Now.AddDays(1)));
    }

    [Fact]
    public void A_long_provider_error_is_truncated()
    {
        var notification = Email();
        notification.MarkAttemptFailed(new string('x', 2000), Now);

        // A provider returning a wall of HTML must not become a wall of HTML in
        // the database.
        Assert.Equal(500, notification.LastError!.Length);
    }

    [Fact]
    public void Sending_clears_the_previous_error()
    {
        var notification = Email();
        notification.MarkAttemptFailed("transient", Now);
        notification.MarkSent(Now.AddSeconds(30));

        Assert.Equal(DeliveryState.Sent, notification.DeliveryState);
        Assert.Null(notification.LastError);
        Assert.False(notification.IsDueForDispatch(Now.AddDays(1)));
    }

    [Fact]
    public void Marking_read_twice_keeps_the_first_timestamp()
    {
        var notification = Email();
        notification.MarkRead(Now);
        notification.MarkRead(Now.AddHours(1));

        Assert.Equal(Now, notification.ReadAt);
    }
}
