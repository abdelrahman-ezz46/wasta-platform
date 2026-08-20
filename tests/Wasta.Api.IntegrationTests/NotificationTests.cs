using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasta.Application.Features.Notifications;
using Wasta.Domain.Audit;
using Wasta.Infrastructure.Notifications;
using Wasta.Infrastructure.Persistence;

namespace Wasta.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public class NotificationTests(WastaApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static string UniqueEmail(string prefix) => $"{prefix}.{Guid.NewGuid():N}@wasta.test";

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }

    private async Task<string> AdminTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = WastaApiFactory.AdminEmail,
            password = WastaApiFactory.AdminPassword,
        });

        return (await ReadJson(response)).GetProperty("accessToken").GetString()!;
    }

    private async Task<int> TrackIdAsync(string slug)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
        return await db.Tracks.Where(t => t.Slug == slug).Select(t => t.Id).FirstAsync();
    }

    private async Task<List<Notification>> NotificationsForAsync(long userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
        return await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderBy(n => n.Id)
            .ToListAsync();
    }

    /// <summary>
    /// Payloads are stored in a jsonb column, which Postgres normalises: keys
    /// are reordered and whitespace is rewritten. Asserting on the raw text
    /// would be testing Postgres's formatter, so read the value instead.
    /// </summary>
    private static string PayloadValue(Notification notification, string property)
    {
        using var document = JsonDocument.Parse(notification.Payload);
        var value = document.RootElement.GetProperty(property);

        return value.ValueKind == JsonValueKind.String ? value.GetString()! : value.ToString();
    }

    private sealed record Actor(long UserId, long ActorId, string Token, string Name);

    private async Task<Actor> NewSeekerAsync(int? trackId = null)
    {
        var name = $"Seeker {Guid.NewGuid():N}";
        var response = await _client.PostAsJsonAsync("/api/auth/register/seeker", new
        {
            fullName = name,
            email = UniqueEmail("seeker"),
            password = "Passw0rd123",
            trackId,
        });

        response.EnsureSuccessStatusCode();
        var body = await ReadJson(response);

        return new Actor(
            body.GetProperty("userId").GetInt64(),
            body.GetProperty("seekerId").GetInt64(),
            body.GetProperty("accessToken").GetString()!,
            name);
    }

    private async Task<Actor> NewCompanyAsync(bool approve)
    {
        var name = $"Company {Guid.NewGuid():N}";
        var response = await _client.PostAsJsonAsync("/api/auth/register/company", new
        {
            companyName = name,
            workEmail = UniqueEmail("hr"),
            password = "Passw0rd123",
        });

        response.EnsureSuccessStatusCode();
        var body = await ReadJson(response);
        var companyId = body.GetProperty("companyId").GetInt64();

        if (approve)
        {
            await SendAsync(HttpMethod.Post, $"/api/admin/companies/{companyId}/approve", await AdminTokenAsync());
        }

        return new Actor(
            body.GetProperty("userId").GetInt64(),
            companyId,
            body.GetProperty("accessToken").GetString()!,
            name);
    }

    private async Task<long> ScoreSeekerAsync(Actor seeker, int trackId)
    {
        var attemptId = (await ReadJson(
                await SendAsync(HttpMethod.Post, $"/api/assessments/tracks/{trackId}/attempts", seeker.Token)))
            .GetProperty("attemptId").GetInt64();

        var view = await ReadJson(
            await SendAsync(HttpMethod.Get, $"/api/assessments/attempts/{attemptId}", seeker.Token));

        foreach (var question in view.GetProperty("questions").EnumerateArray())
        {
            var correct = question.GetProperty("options").EnumerateArray()
                .First(o => o.GetProperty("body").GetString() == "Correct option")
                .GetProperty("optionId").GetInt64();

            await SendAsync(
                HttpMethod.Put,
                $"/api/assessments/attempts/{attemptId}/answers/{question.GetProperty("questionId").GetInt64()}",
                seeker.Token,
                new { selectedOptionId = correct, flaggedForReview = false });
        }

        await SendAsync(HttpMethod.Post, $"/api/assessments/attempts/{attemptId}/submit", seeker.Token);
        return attemptId;
    }

    // ---------- notifications are raised by the right events ----------

    [Fact]
    public async Task Submitting_an_assessment_notifies_the_seeker()
    {
        var trackId = await TrackIdAsync("frontend-engineering");
        var seeker = await NewSeekerAsync(trackId);

        await ScoreSeekerAsync(seeker, trackId);

        var notifications = await NotificationsForAsync(seeker.UserId);
        var results = Assert.Single(notifications, n => n.Kind == NotificationKinds.ResultsReady);

        Assert.Equal("100", PayloadValue(results, "overallPercent"));
        Assert.Equal(DeliveryState.Pending, results.DeliveryState);
    }

    [Fact]
    public async Task Being_unlocked_tells_the_seeker_which_company_did_it()
    {
        var trackId = await TrackIdAsync("data-science");
        var seeker = await NewSeekerAsync(trackId);
        await ScoreSeekerAsync(seeker, trackId);

        var company = await NewCompanyAsync(approve: true);
        await SendAsync(HttpMethod.Post, $"/api/talent-pool/{seeker.ActorId}/unlock", company.Token);

        var notifications = await NotificationsForAsync(seeker.UserId);
        var unlocked = Assert.Single(notifications, n => n.Kind == NotificationKinds.ProfileUnlocked);

        Assert.Equal(company.Name, PayloadValue(unlocked, "companyName"));
    }

    [Fact]
    public async Task A_refused_unlock_leaves_no_notification_behind()
    {
        var trackId = await TrackIdAsync("devops");
        var company = await NewCompanyAsync(approve: true);

        // Spend the three trial credits, then try a fourth candidate.
        for (var i = 0; i < 3; i++)
        {
            var spent = await NewSeekerAsync(trackId);
            await ScoreSeekerAsync(spent, trackId);
            await SendAsync(HttpMethod.Post, $"/api/talent-pool/{spent.ActorId}/unlock", company.Token);
        }

        var unaffordable = await NewSeekerAsync(trackId);
        await ScoreSeekerAsync(unaffordable, trackId);

        var refused = await SendAsync(
            HttpMethod.Post, $"/api/talent-pool/{unaffordable.ActorId}/unlock", company.Token);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        // The notification is written inside the unlock transaction, so a
        // rolled-back charge cannot leave "a company viewed your profile"
        // behind. Telling someone they were viewed when nobody paid to view
        // them would be a lie the database told.
        var notifications = await NotificationsForAsync(unaffordable.UserId);
        Assert.DoesNotContain(notifications, n => n.Kind == NotificationKinds.ProfileUnlocked);
    }

    [Fact]
    public async Task An_application_status_change_notifies_the_applicant()
    {
        var trackId = await TrackIdAsync("backend-engineering");
        var company = await NewCompanyAsync(approve: true);
        var seeker = await NewSeekerAsync(trackId);

        var jobId = (await ReadJson(await SendAsync(
                HttpMethod.Post, "/api/companies/me/jobs", company.Token,
                new { title = "Reviewer bait", trackId, jobDescription = "Work." })))
            .GetProperty("jobPostId").GetInt64();

        var applicationId = (await ReadJson(
                await SendAsync(HttpMethod.Post, $"/api/jobs/{jobId}/apply", seeker.Token)))
            .GetProperty("applicationId").GetInt64();

        await SendAsync(
            HttpMethod.Put, $"/api/companies/me/applications/{applicationId}/status", company.Token,
            new { statusId = 2, feedback = "Shortlisted." });

        var notifications = await NotificationsForAsync(seeker.UserId);
        var changed = Assert.Single(notifications, n => n.Kind == NotificationKinds.ApplicationStatusChanged);

        Assert.Equal("Reviewer bait", PayloadValue(changed, "jobTitle"));
        Assert.Equal("In review", PayloadValue(changed, "status"));
    }

    [Fact]
    public async Task Verification_and_credit_decisions_notify_the_company()
    {
        var company = await NewCompanyAsync(approve: true);

        var approved = await NotificationsForAsync(company.UserId);
        Assert.Single(approved, n => n.Kind == NotificationKinds.CompanyApproved);

        int paymentMethodId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
            paymentMethodId = await db.PaymentMethods.Select(p => p.Id).FirstAsync();
        }

        var requestId = (await ReadJson(await SendAsync(
                HttpMethod.Post, "/api/companies/me/credits/topups", company.Token,
                new { creditsRequested = 7, paymentMethodId, amount = 100m, currency = "EGP" })))
            .GetProperty("requestId").GetInt64();

        await SendAsync(
            HttpMethod.Post, $"/api/admin/topups/{requestId}/review", await AdminTokenAsync(),
            new { approve = true, note = "Received." });

        var afterTopUp = await NotificationsForAsync(company.UserId);
        var issued = Assert.Single(afterTopUp, n => n.Kind == NotificationKinds.CreditsIssued);

        Assert.Equal("7", PayloadValue(issued, "credits"));
        Assert.Equal("10", PayloadValue(issued, "balance"));
    }

    [Fact]
    public async Task A_rejected_company_is_told_why()
    {
        var company = await NewCompanyAsync(approve: false);

        await SendAsync(
            HttpMethod.Post, $"/api/admin/companies/{company.ActorId}/reject", await AdminTokenAsync(),
            new { note = "The commercial register was unreadable." });

        var notifications = await NotificationsForAsync(company.UserId);
        var rejected = Assert.Single(notifications, n => n.Kind == NotificationKinds.CompanyRejected);

        Assert.Contains("unreadable", PayloadValue(rejected, "note"));
    }

    // ---------- reading them ----------

    [Fact]
    public async Task A_user_sees_only_their_own_notifications()
    {
        var trackId = await TrackIdAsync("ui-ux-design");
        var mine = await NewSeekerAsync(trackId);
        var theirs = await NewSeekerAsync(trackId);

        await ScoreSeekerAsync(mine, trackId);
        await ScoreSeekerAsync(theirs, trackId);

        var listed = await ReadJson(await SendAsync(HttpMethod.Get, "/api/notifications", mine.Token));

        Assert.Equal(1, listed.GetProperty("totalCount").GetInt32());

        var theirNotification = (await NotificationsForAsync(theirs.UserId)).First();
        var stolen = await SendAsync(
            HttpMethod.Post, $"/api/notifications/{theirNotification.Id}/read", mine.Token);

        Assert.Equal(HttpStatusCode.NotFound, stolen.StatusCode);
    }

    [Fact]
    public async Task Notifications_can_be_marked_read_individually_and_all_at_once()
    {
        var trackId = await TrackIdAsync("product-management");
        var seeker = await NewSeekerAsync(trackId);
        await ScoreSeekerAsync(seeker, trackId);

        var before = await ReadJson(
            await SendAsync(HttpMethod.Get, "/api/notifications/unread-count", seeker.Token));
        Assert.Equal(1, before.GetProperty("unread").GetInt32());

        var notificationId = (await NotificationsForAsync(seeker.UserId)).First().Id;
        var read = await SendAsync(
            HttpMethod.Post, $"/api/notifications/{notificationId}/read", seeker.Token);
        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);

        var after = await ReadJson(
            await SendAsync(HttpMethod.Get, "/api/notifications/unread-count", seeker.Token));
        Assert.Equal(0, after.GetProperty("unread").GetInt32());

        var readAll = await ReadJson(
            await SendAsync(HttpMethod.Post, "/api/notifications/read-all", seeker.Token));
        Assert.Equal(0, readAll.GetProperty("marked").GetInt32());
    }

    [Fact]
    public async Task An_anonymous_caller_gets_nothing()
    {
        Assert.Equal(
            HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/notifications")).StatusCode);
    }

    // ---------- dispatch ----------

    [Fact]
    public async Task The_dispatcher_delivers_pending_notifications_and_marks_them_sent()
    {
        var trackId = await TrackIdAsync("frontend-engineering");
        var seeker = await NewSeekerAsync(trackId);
        await ScoreSeekerAsync(seeker, trackId);

        Assert.All(
            await NotificationsForAsync(seeker.UserId),
            n => Assert.Equal(DeliveryState.Pending, n.DeliveryState));

        using (var scope = factory.Services.CreateScope())
        {
            await NotificationDispatcher.DispatchBatchAsync(
                scope.ServiceProvider, batchSize: 100, CancellationToken.None);
        }

        var dispatched = await NotificationsForAsync(seeker.UserId);
        Assert.All(dispatched, n =>
        {
            Assert.Equal(DeliveryState.Sent, n.DeliveryState);
            Assert.Equal(1, n.Attempts);
            Assert.NotNull(n.DispatchedAt);
        });
    }

    [Fact]
    public async Task Dispatching_twice_does_not_resend()
    {
        var trackId = await TrackIdAsync("devops");
        var seeker = await NewSeekerAsync(trackId);
        await ScoreSeekerAsync(seeker, trackId);

        using (var scope = factory.Services.CreateScope())
        {
            await NotificationDispatcher.DispatchBatchAsync(
                scope.ServiceProvider, 100, CancellationToken.None);
        }

        using (var scope = factory.Services.CreateScope())
        {
            await NotificationDispatcher.DispatchBatchAsync(
                scope.ServiceProvider, 100, CancellationToken.None);
        }

        // One attempt each, not two: a delivered row is no longer pending, so a
        // second pass must not pick it up and mail the person again.
        Assert.All(await NotificationsForAsync(seeker.UserId), n => Assert.Equal(1, n.Attempts));
    }
}
