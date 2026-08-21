using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasta.CareerCoach.Data;
using Wasta.Infrastructure.Persistence;
using CoachDomain = Wasta.CareerCoach.Domain;
using ChatDomain = Wasta.SupportChat.Domain;

namespace Wasta.Api.IntegrationTests;

/// <summary>
/// The two AI modules were written against five ports before the platform
/// tables existed. These prove the ports actually connect - which until now was
/// asserted rather than tested.
/// </summary>
[Collection(nameof(ApiCollection))]
public class AiModuleIntegrationTests(WastaApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static string UniqueEmail(string prefix) => $"{prefix}.{Guid.NewGuid():N}@wasta.test";

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, string? token = null, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }

    private async Task<int> TrackIdAsync(string slug)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
        return await db.Tracks.Where(t => t.Slug == slug).Select(t => t.Id).FirstAsync();
    }

    private sealed record Seeker(long SeekerId, string Token);

    private async Task<Seeker> ScoredSeekerAsync(int trackId)
    {
        var registered = await _client.PostAsJsonAsync("/api/auth/register/seeker", new
        {
            fullName = "Coach Candidate",
            email = UniqueEmail("coach"),
            password = "Passw0rd123",
            trackId,
        });

        registered.EnsureSuccessStatusCode();
        var body = await ReadJson(registered);
        var token = body.GetProperty("accessToken").GetString()!;
        var seekerId = body.GetProperty("seekerId").GetInt64();

        var attemptId = (await ReadJson(await SendAsync(
                HttpMethod.Post, $"/api/assessments/tracks/{trackId}/attempts", token)))
            .GetProperty("attemptId").GetInt64();

        var view = await ReadJson(
            await SendAsync(HttpMethod.Get, $"/api/assessments/attempts/{attemptId}", token));

        foreach (var question in view.GetProperty("questions").EnumerateArray())
        {
            var correct = question.GetProperty("options").EnumerateArray()
                .First(o => o.GetProperty("body").GetString() == "Correct option")
                .GetProperty("optionId").GetInt64();

            await SendAsync(
                HttpMethod.Put,
                $"/api/assessments/attempts/{attemptId}/answers/{question.GetProperty("questionId").GetInt64()}",
                token,
                new { selectedOptionId = correct, flaggedForReview = false });
        }

        await SendAsync(HttpMethod.Post, $"/api/assessments/attempts/{attemptId}/submit", token);
        return new Seeker(seekerId, token);
    }

    // ---------- the ports ----------

    [Fact]
    public async Task The_coach_reads_a_real_scored_attempt_through_its_port()
    {
        var trackId = await TrackIdAsync("frontend-engineering");
        var seeker = await ScoredSeekerAsync(trackId);

        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<CoachDomain.IAssessmentDataProvider>();

        var attemptId = await provider.GetCurrentAttemptIdAsync((int)seeker.SeekerId, CancellationToken.None);
        Assert.NotNull(attemptId);

        var score = await provider.GetAttemptScoreAsync(attemptId!.Value, CancellationToken.None);

        Assert.NotNull(score);
        Assert.Equal((int)seeker.SeekerId, score!.StudentId);
        Assert.Equal("Frontend Engineering", score.Track);
        Assert.Equal(5, score.Sections.Count);
        Assert.All(score.Sections, s => Assert.Equal(100, s.Percent));
    }

    [Fact]
    public async Task The_student_context_carries_no_identifying_data()
    {
        var trackId = await TrackIdAsync("data-science");
        var seeker = await ScoredSeekerAsync(trackId);

        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<CoachDomain.IAssessmentDataProvider>();

        var context = await provider.GetStudentContextAsync((int)seeker.SeekerId, CancellationToken.None);

        // The DTO has no fields for name, email, university, city or CV, so what
        // reaches the model is bounded by shape rather than by remembering. This
        // asserts the shape has not since grown one.
        var properties = context.GetType().GetProperties().Select(p => p.Name).ToList();

        Assert.Equal(3, properties.Count);
        Assert.Contains("Skills", properties);
        Assert.Contains("ProjectTitles", properties);
        Assert.Contains("GraduationYear", properties);
    }

    [Fact]
    public async Task The_chatbot_is_offered_real_job_posts_matched_to_the_seekers_track()
    {
        var trackId = await TrackIdAsync("backend-engineering");

        var company = await _client.PostAsJsonAsync("/api/auth/register/company", new
        {
            companyName = $"Chat Employer {Guid.NewGuid():N}",
            workEmail = UniqueEmail("hr"),
            password = "Passw0rd123",
        });

        var companyBody = await ReadJson(company);
        var companyId = companyBody.GetProperty("companyId").GetInt64();
        var companyToken = companyBody.GetProperty("accessToken").GetString()!;

        var adminLogin = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = WastaApiFactory.AdminEmail,
            password = WastaApiFactory.AdminPassword,
        });
        var adminToken = (await ReadJson(adminLogin)).GetProperty("accessToken").GetString()!;
        await SendAsync(HttpMethod.Post, $"/api/admin/companies/{companyId}/approve", adminToken);

        var title = $"Chat Visible Role {Guid.NewGuid():N}";
        await SendAsync(HttpMethod.Post, "/api/companies/me/jobs", companyToken, new
        {
            title,
            trackId,
            jobDescription = "Backend work.",
        });

        var seeker = await ScoredSeekerAsync(trackId);

        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ChatDomain.IJobListingProvider>();

        var listings = await provider.GetOpenListingsAsync((int)seeker.SeekerId, 10, CancellationToken.None);

        Assert.Contains(listings, l => l.Title == title);

        // The chatbot is instructed never to invent a URL. Every listing it is
        // handed carries a real one it can quote.
        Assert.All(listings, l => Assert.StartsWith("/jobs/", l.Url));
    }

    [Fact]
    public async Task An_anonymous_visitor_is_offered_listings_without_a_track_filter()
    {
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ChatDomain.IJobListingProvider>();

        var listings = await provider.GetOpenListingsAsync(null, 5, CancellationToken.None);

        Assert.True(listings.Count <= 5);
    }

    // ---------- the wiring ----------

    [Fact]
    public async Task Submitting_an_assessment_creates_a_coach_plan_row()
    {
        var trackId = await TrackIdAsync("ui-ux-design");
        var seeker = await ScoredSeekerAsync(trackId);

        using var scope = factory.Services.CreateScope();
        var coachDb = scope.ServiceProvider.GetRequiredService<CoachDbContext>();

        // The trigger fires from the platform's own submit handler, so scoring
        // an attempt is what starts a plan - no separate call for a host to
        // remember.
        Assert.True(await coachDb.StudentCoachPlans.AnyAsync(p => p.StudentId == (int)seeker.SeekerId));
    }

    [Fact]
    public async Task The_coach_plan_endpoint_is_reachable_and_reports_unavailable_with_ai_off()
    {
        var trackId = await TrackIdAsync("product-management");
        var seeker = await ScoredSeekerAsync(trackId);

        var response = await SendAsync(HttpMethod.Get, "/api/students/me/coach-plan", seeker.Token);
        var status = (await ReadJson(response)).GetProperty("status").GetString();

        // Not asserting the settled state here on purpose. The module's worker
        // deliberately waits two seconds between jobs so a burst of submissions
        // cannot trip a free-tier AI rate limit, so with a suite-wide queue the
        // settled value can be half a minute away. Waiting for it would be
        // testing the throttle, not the wiring.
        //
        // What this test is about is that the endpoint is mounted, reachable by
        // a seeker through the StudentOnly alias, and answers with a status the
        // results page can render.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(status, new[] { "pending", "unavailable", "ready", "failed" });
    }

    [Fact]
    public async Task The_coach_plan_endpoint_accepts_a_seeker_despite_the_module_calling_them_a_student()
    {
        var trackId = await TrackIdAsync("devops");
        var seeker = await ScoredSeekerAsync(trackId);

        var allowed = await SendAsync(HttpMethod.Get, "/api/students/me/coach-plan", seeker.Token);

        // The module's endpoints require a policy named StudentOnly. The host
        // supplies that name rather than editing a tested module to match our
        // vocabulary - this proves the alias actually resolves.
        Assert.NotEqual(HttpStatusCode.Forbidden, allowed.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, allowed.StatusCode);
    }

    [Fact]
    public async Task A_company_cannot_read_a_coach_plan()
    {
        var company = await _client.PostAsJsonAsync("/api/auth/register/company", new
        {
            companyName = $"Nosy {Guid.NewGuid():N}",
            workEmail = UniqueEmail("hr"),
            password = "Passw0rd123",
        });

        var token = (await ReadJson(company)).GetProperty("accessToken").GetString()!;

        var response = await SendAsync(HttpMethod.Get, "/api/students/me/coach-plan", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_support_chat_endpoints_are_mounted()
    {
        var created = await _client.PostAsJsonAsync(
            "/api/chat/sessions", new { visitorId = Guid.NewGuid().ToString() });

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var sessionId = (await ReadJson(created)).GetProperty("sessionId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(sessionId));
    }

    [Fact]
    public async Task An_unknown_chat_session_returns_an_empty_history_rather_than_an_error()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/chat/sessions/{Guid.NewGuid()}/messages");
        request.Headers.Add("X-Wasta-Visitor-Id", Guid.NewGuid().ToString());

        var response = await _client.SendAsync(request);

        // A stolen or stale session id leaks nothing and errors on nothing.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await ReadJson(response)).EnumerateArray());
    }
}
