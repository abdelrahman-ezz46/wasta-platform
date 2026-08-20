using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasta.Infrastructure.Persistence;

namespace Wasta.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public class JobsAndApplicationsTests(WastaApiFactory factory)
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

    private async Task<int> TrackIdAsync(string slug)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
        return await db.Tracks.Where(t => t.Slug == slug).Select(t => t.Id).FirstAsync();
    }

    private async Task<(long SeekerId, string Token)> NewSeekerAsync(int? trackId = null)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register/seeker", new
        {
            fullName = "Applicant",
            email = UniqueEmail("seeker"),
            password = "Passw0rd123",
            trackId,
        });

        response.EnsureSuccessStatusCode();
        var body = await ReadJson(response);
        return (body.GetProperty("seekerId").GetInt64(), body.GetProperty("accessToken").GetString()!);
    }

    /// <summary>Registers a company and approves it, since posting requires verification.</summary>
    private async Task<(long CompanyId, string Token)> NewVerifiedCompanyAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register/company", new
        {
            companyName = $"Employer {Guid.NewGuid():N}",
            workEmail = UniqueEmail("hr"),
            password = "Passw0rd123",
        });

        response.EnsureSuccessStatusCode();
        var body = await ReadJson(response);
        var companyId = body.GetProperty("companyId").GetInt64();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
            var company = await db.Companies.FirstAsync(c => c.Id == companyId);
            company.Approve(adminUserId: 1, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        return (companyId, body.GetProperty("accessToken").GetString()!);
    }

    private async Task<long> PostJobAsync(string companyToken, int trackId, string? title = null)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/companies/me/jobs", companyToken, new
        {
            title = title ?? $"Engineer {Guid.NewGuid():N}",
            trackId,
            jobDescription = "Build things.",
        });

        response.EnsureSuccessStatusCode();
        return (await ReadJson(response)).GetProperty("jobPostId").GetInt64();
    }

    // ---------- posting ----------

    [Fact]
    public async Task A_verified_company_can_post_a_job()
    {
        var (_, token) = await NewVerifiedCompanyAsync();
        var trackId = await TrackIdAsync("frontend-engineering");

        var response = await SendAsync(HttpMethod.Post, "/api/companies/me/jobs", token, new
        {
            title = "Junior Frontend Engineer",
            trackId,
            jobDescription = "React and TypeScript.",
            salary = new { min = 25000m, max = 35000m, currency = "EGP", period = "month" },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task An_unverified_company_cannot_post()
    {
        var registered = await _client.PostAsJsonAsync("/api/auth/register/company", new
        {
            companyName = $"Unverified {Guid.NewGuid():N}",
            workEmail = UniqueEmail("hr"),
            password = "Passw0rd123",
        });
        var token = (await ReadJson(registered)).GetProperty("accessToken").GetString()!;
        var trackId = await TrackIdAsync("frontend-engineering");

        var response = await SendAsync(HttpMethod.Post, "/api/companies/me/jobs", token, new
        {
            title = "Should not exist",
            trackId,
            jobDescription = "No.",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_salary_without_a_currency_is_refused()
    {
        var (_, token) = await NewVerifiedCompanyAsync();
        var trackId = await TrackIdAsync("frontend-engineering");

        var response = await SendAsync(HttpMethod.Post, "/api/companies/me/jobs", token, new
        {
            title = "No currency",
            trackId,
            jobDescription = "Pay unclear.",
            salary = new { min = 100m, max = 200m, currency = (string?)null, period = "month" },
        });

        // Four currencies are in play across Egypt, the UAE, Jordan and Saudi.
        // A bare number would be displayed as whichever the reader assumes.
        // A broken business rule is a 400 carrying its code, never a 500.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "job.salary_currency_required", (await ReadJson(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_sixth_post_is_the_last_one_allowed()
    {
        var (_, token) = await NewVerifiedCompanyAsync();
        var trackId = await TrackIdAsync("frontend-engineering");

        for (var i = 0; i < 6; i++)
        {
            await PostJobAsync(token, trackId);
        }

        var seventh = await SendAsync(HttpMethod.Post, "/api/companies/me/jobs", token, new
        {
            title = "One too many",
            trackId,
            jobDescription = "Over the cap.",
        });

        Assert.Equal(HttpStatusCode.Conflict, seventh.StatusCode);
        Assert.Equal("job.active_limit_reached", (await ReadJson(seventh)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Closing_a_post_frees_a_slot()
    {
        var (_, token) = await NewVerifiedCompanyAsync();
        var trackId = await TrackIdAsync("frontend-engineering");

        var first = await PostJobAsync(token, trackId);
        for (var i = 0; i < 5; i++)
        {
            await PostJobAsync(token, trackId);
        }

        var blocked = await SendAsync(HttpMethod.Post, "/api/companies/me/jobs", token, new
        {
            title = "Blocked",
            trackId,
            jobDescription = "At the cap.",
        });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        var closed = await SendAsync(HttpMethod.Post, $"/api/companies/me/jobs/{first}/close", token);
        Assert.Equal(HttpStatusCode.NoContent, closed.StatusCode);

        var allowed = await SendAsync(HttpMethod.Post, "/api/companies/me/jobs", token, new
        {
            title = "Now allowed",
            trackId,
            jobDescription = "Slot freed.",
        });
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }

    [Fact]
    public async Task Another_companys_post_reports_not_found()
    {
        var (_, ownerToken) = await NewVerifiedCompanyAsync();
        var (_, intruderToken) = await NewVerifiedCompanyAsync();
        var trackId = await TrackIdAsync("frontend-engineering");
        var jobId = await PostJobAsync(ownerToken, trackId);

        var edit = await SendAsync(HttpMethod.Put, $"/api/companies/me/jobs/{jobId}", intruderToken, new
        {
            title = "Hijacked",
            jobDescription = "Not yours.",
        });
        var close = await SendAsync(HttpMethod.Post, $"/api/companies/me/jobs/{jobId}/close", intruderToken);
        var applicants = await SendAsync(HttpMethod.Get, $"/api/companies/me/jobs/{jobId}/applicants", intruderToken);

        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, close.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, applicants.StatusCode);
    }

    // ---------- browsing ----------

    [Fact]
    public async Task A_post_on_the_seekers_own_track_is_flagged_recommended()
    {
        var frontend = await TrackIdAsync("frontend-engineering");
        var devops = await TrackIdAsync("devops");
        var (_, companyToken) = await NewVerifiedCompanyAsync();

        var mine = await PostJobAsync(companyToken, frontend, $"Recommended {Guid.NewGuid():N}");
        var other = await PostJobAsync(companyToken, devops, $"Unrelated {Guid.NewGuid():N}");

        var (_, seekerToken) = await NewSeekerAsync(frontend);

        var onTrack = await ReadJson(await SendAsync(HttpMethod.Get, $"/api/jobs/{mine}", seekerToken));
        var offTrack = await ReadJson(await SendAsync(HttpMethod.Get, $"/api/jobs/{other}", seekerToken));

        Assert.True(onTrack.GetProperty("summary").GetProperty("isRecommended").GetBoolean());
        Assert.False(offTrack.GetProperty("summary").GetProperty("isRecommended").GetBoolean());
    }

    [Fact]
    public async Task Browsing_finds_a_post_by_title_and_pages_the_result()
    {
        var trackId = await TrackIdAsync("backend-engineering");
        var (_, companyToken) = await NewVerifiedCompanyAsync();
        var marker = $"Kestrel{Guid.NewGuid():N}";
        await PostJobAsync(companyToken, trackId, marker);

        var (_, seekerToken) = await NewSeekerAsync(trackId);
        var body = await ReadJson(
            await SendAsync(HttpMethod.Get, $"/api/jobs?search={marker}&pageSize=5", seekerToken));

        Assert.Equal(1, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(5, body.GetProperty("pageSize").GetInt32());
        Assert.Equal(marker, body.GetProperty("items")[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_closed_post_disappears_from_browsing()
    {
        var trackId = await TrackIdAsync("devops");
        var (_, companyToken) = await NewVerifiedCompanyAsync();
        var marker = $"Closing{Guid.NewGuid():N}";
        var jobId = await PostJobAsync(companyToken, trackId, marker);

        var (_, seekerToken) = await NewSeekerAsync(trackId);
        var before = await ReadJson(await SendAsync(HttpMethod.Get, $"/api/jobs?search={marker}", seekerToken));
        Assert.Equal(1, before.GetProperty("totalCount").GetInt32());

        await SendAsync(HttpMethod.Post, $"/api/companies/me/jobs/{jobId}/close", companyToken);

        var after = await ReadJson(await SendAsync(HttpMethod.Get, $"/api/jobs?search={marker}", seekerToken));
        Assert.Equal(0, after.GetProperty("totalCount").GetInt32());
    }

    // ---------- applying ----------

    [Fact]
    public async Task Applying_creates_an_application_and_flags_the_post_as_applied()
    {
        var trackId = await TrackIdAsync("frontend-engineering");
        var (_, companyToken) = await NewVerifiedCompanyAsync();
        var jobId = await PostJobAsync(companyToken, trackId);
        var (_, seekerToken) = await NewSeekerAsync(trackId);

        var applied = await SendAsync(HttpMethod.Post, $"/api/jobs/{jobId}/apply", seekerToken);
        Assert.Equal(HttpStatusCode.Created, applied.StatusCode);

        var detail = await ReadJson(await SendAsync(HttpMethod.Get, $"/api/jobs/{jobId}", seekerToken));
        Assert.True(detail.GetProperty("summary").GetProperty("hasApplied").GetBoolean());
    }

    [Fact]
    public async Task Applying_to_a_closed_post_is_refused()
    {
        var trackId = await TrackIdAsync("frontend-engineering");
        var (_, companyToken) = await NewVerifiedCompanyAsync();
        var jobId = await PostJobAsync(companyToken, trackId);
        await SendAsync(HttpMethod.Post, $"/api/companies/me/jobs/{jobId}/close", companyToken);

        var (_, seekerToken) = await NewSeekerAsync(trackId);
        var response = await SendAsync(HttpMethod.Post, $"/api/jobs/{jobId}/apply", seekerToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("job.closed", (await ReadJson(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_seventh_live_application_is_refused()
    {
        var trackId = await TrackIdAsync("frontend-engineering");
        var (_, companyToken) = await NewVerifiedCompanyAsync();
        var (_, seekerToken) = await NewSeekerAsync(trackId);

        // Six postings, six applications - the cap is on the seeker, not the post.
        var jobs = new List<long>();
        for (var i = 0; i < 6; i++)
        {
            jobs.Add(await PostJobAsync(companyToken, trackId));
        }

        foreach (var jobId in jobs)
        {
            var ok = await SendAsync(HttpMethod.Post, $"/api/jobs/{jobId}/apply", seekerToken);
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }

        var (_, otherCompanyToken) = await NewVerifiedCompanyAsync();
        var seventhJob = await PostJobAsync(otherCompanyToken, trackId);

        var seventh = await SendAsync(HttpMethod.Post, $"/api/jobs/{seventhJob}/apply", seekerToken);

        Assert.Equal(HttpStatusCode.Conflict, seventh.StatusCode);
        Assert.Equal("application.limit_reached", (await ReadJson(seventh)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Withdrawing_frees_a_slot_and_reapplying_creates_a_second_application()
    {
        var trackId = await TrackIdAsync("frontend-engineering");
        var (_, companyToken) = await NewVerifiedCompanyAsync();
        var jobId = await PostJobAsync(companyToken, trackId);
        var (_, seekerToken) = await NewSeekerAsync(trackId);

        var first = (await ReadJson(await SendAsync(HttpMethod.Post, $"/api/jobs/{jobId}/apply", seekerToken)))
            .GetProperty("applicationId").GetInt64();

        await SendAsync(HttpMethod.Post, $"/api/seekers/me/applications/{first}/withdraw", seekerToken);

        var second = (await ReadJson(await SendAsync(HttpMethod.Post, $"/api/jobs/{jobId}/apply", seekerToken)))
            .GetProperty("applicationId").GetInt64();

        // A new row, not a reused one: the earlier submitted work survives
        // alongside the fresh attempt.
        Assert.NotEqual(first, second);

        var list = await ReadJson(
            await SendAsync(HttpMethod.Get, "/api/seekers/me/applications", seekerToken));
        Assert.Equal(2, list.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task A_project_can_be_saved_and_submitted()
    {
        var trackId = await TrackIdAsync("frontend-engineering");
        var (_, companyToken) = await NewVerifiedCompanyAsync();
        var jobId = await PostJobAsync(companyToken, trackId);
        var (_, seekerToken) = await NewSeekerAsync(trackId);

        var applicationId = (await ReadJson(await SendAsync(HttpMethod.Post, $"/api/jobs/{jobId}/apply", seekerToken)))
            .GetProperty("applicationId").GetInt64();

        var saved = await SendAsync(
            HttpMethod.Put, $"/api/seekers/me/applications/{applicationId}", seekerToken, new
            {
                projectTitle = "Landing page redesign",
                description = "Rebuilt the marketing site.",
                repoUrl = "https://github.com/example/repo",
                liveDemoUrl = "https://example.com",
            });
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);

        var submitted = await SendAsync(
            HttpMethod.Post, $"/api/seekers/me/applications/{applicationId}/submit", seekerToken);
        Assert.Equal(HttpStatusCode.NoContent, submitted.StatusCode);

        var view = await ReadJson(
            await SendAsync(HttpMethod.Get, $"/api/seekers/me/applications/{applicationId}", seekerToken));
        Assert.Equal("Landing page redesign", view.GetProperty("projectTitle").GetString());
        Assert.NotEqual(JsonValueKind.Null, view.GetProperty("submittedAt").ValueKind);
    }

    [Fact]
    public async Task An_over_long_project_description_is_refused()
    {
        var trackId = await TrackIdAsync("frontend-engineering");
        var (_, companyToken) = await NewVerifiedCompanyAsync();
        var jobId = await PostJobAsync(companyToken, trackId);
        var (_, seekerToken) = await NewSeekerAsync(trackId);

        var applicationId = (await ReadJson(await SendAsync(HttpMethod.Post, $"/api/jobs/{jobId}/apply", seekerToken)))
            .GetProperty("applicationId").GetInt64();

        var response = await SendAsync(
            HttpMethod.Put, $"/api/seekers/me/applications/{applicationId}", seekerToken, new
            {
                description = new string('x', 601),
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "application.description_too_long", (await ReadJson(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Another_seekers_application_reports_not_found()
    {
        var trackId = await TrackIdAsync("frontend-engineering");
        var (_, companyToken) = await NewVerifiedCompanyAsync();
        var jobId = await PostJobAsync(companyToken, trackId);
        var (_, ownerToken) = await NewSeekerAsync(trackId);
        var (_, intruderToken) = await NewSeekerAsync(trackId);

        var applicationId = (await ReadJson(await SendAsync(HttpMethod.Post, $"/api/jobs/{jobId}/apply", ownerToken)))
            .GetProperty("applicationId").GetInt64();

        var read = await SendAsync(HttpMethod.Get, $"/api/seekers/me/applications/{applicationId}", intruderToken);
        var edit = await SendAsync(
            HttpMethod.Put, $"/api/seekers/me/applications/{applicationId}", intruderToken, new { description = "mine now" });
        var withdraw = await SendAsync(
            HttpMethod.Post, $"/api/seekers/me/applications/{applicationId}/withdraw", intruderToken);

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, withdraw.StatusCode);
    }

    // ---------- company review ----------

    [Fact]
    public async Task Applicants_are_anonymous_until_unlocked()
    {
        var trackId = await TrackIdAsync("frontend-engineering");
        var (_, companyToken) = await NewVerifiedCompanyAsync();
        var jobId = await PostJobAsync(companyToken, trackId);
        var (_, seekerToken) = await NewSeekerAsync(trackId);
        await SendAsync(HttpMethod.Post, $"/api/jobs/{jobId}/apply", seekerToken);

        var response = await SendAsync(HttpMethod.Get, $"/api/companies/me/jobs/{jobId}/applicants", companyToken);
        var raw = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(raw).RootElement;
        var applicant = body.GetProperty("items")[0];

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(applicant.GetProperty("isUnlocked").GetBoolean());
        Assert.Equal(JsonValueKind.Null, applicant.GetProperty("fullName").ValueKind);
        Assert.StartsWith("#", applicant.GetProperty("candidateReference").GetString());

        // The seeker's real name must not appear anywhere in the payload.
        Assert.DoesNotContain("Applicant", raw);
    }

    [Fact]
    public async Task A_company_can_move_an_applicant_through_review()
    {
        var trackId = await TrackIdAsync("frontend-engineering");
        var (_, companyToken) = await NewVerifiedCompanyAsync();
        var jobId = await PostJobAsync(companyToken, trackId);
        var (_, seekerToken) = await NewSeekerAsync(trackId);

        var applicationId = (await ReadJson(await SendAsync(HttpMethod.Post, $"/api/jobs/{jobId}/apply", seekerToken)))
            .GetProperty("applicationId").GetInt64();

        var response = await SendAsync(
            HttpMethod.Put, $"/api/companies/me/applications/{applicationId}/status", companyToken,
            new { statusId = 2, feedback = "Nice project, moving forward." });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var view = await ReadJson(
            await SendAsync(HttpMethod.Get, $"/api/seekers/me/applications/{applicationId}", seekerToken));
        Assert.Equal(2, view.GetProperty("statusId").GetInt32());
        Assert.Equal("Nice project, moving forward.", view.GetProperty("feedback").GetString());
    }

    [Fact]
    public async Task A_company_cannot_withdraw_on_the_applicants_behalf()
    {
        var trackId = await TrackIdAsync("frontend-engineering");
        var (_, companyToken) = await NewVerifiedCompanyAsync();
        var jobId = await PostJobAsync(companyToken, trackId);
        var (_, seekerToken) = await NewSeekerAsync(trackId);

        var applicationId = (await ReadJson(await SendAsync(HttpMethod.Post, $"/api/jobs/{jobId}/apply", seekerToken)))
            .GetProperty("applicationId").GetInt64();

        var response = await SendAsync(
            HttpMethod.Put, $"/api/companies/me/applications/{applicationId}/status", companyToken,
            new { statusId = 5, feedback = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "application.status_not_allowed", (await ReadJson(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_company_cannot_review_another_companys_applicant()
    {
        var trackId = await TrackIdAsync("frontend-engineering");
        var (_, ownerToken) = await NewVerifiedCompanyAsync();
        var (_, intruderToken) = await NewVerifiedCompanyAsync();
        var jobId = await PostJobAsync(ownerToken, trackId);
        var (_, seekerToken) = await NewSeekerAsync(trackId);

        var applicationId = (await ReadJson(await SendAsync(HttpMethod.Post, $"/api/jobs/{jobId}/apply", seekerToken)))
            .GetProperty("applicationId").GetInt64();

        var response = await SendAsync(
            HttpMethod.Put, $"/api/companies/me/applications/{applicationId}/status", intruderToken,
            new { statusId = 3, feedback = "Rejected by a stranger." });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_seeker_cannot_reach_the_company_job_endpoints()
    {
        var trackId = await TrackIdAsync("frontend-engineering");
        var (_, seekerToken) = await NewSeekerAsync(trackId);

        var response = await SendAsync(HttpMethod.Get, "/api/companies/me/jobs", seekerToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
