using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasta.Infrastructure.Persistence;

namespace Wasta.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public class AssessmentTests(WastaApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static string UniqueEmail(string prefix) => $"{prefix}.{Guid.NewGuid():N}@wasta.test";

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private sealed record Seeker(long Id, string AccessToken);

    private async Task<Seeker> NewSeekerAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register/seeker", new
        {
            fullName = "Assessment Taker",
            email = UniqueEmail("taker"),
            password = "Passw0rd123",
        });

        response.EnsureSuccessStatusCode();
        var body = await ReadJson(response);
        return new Seeker(body.GetProperty("seekerId").GetInt64(), body.GetProperty("accessToken").GetString()!);
    }

    private async Task<int> FrontendTrackIdAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
        return await db.Tracks.Where(t => t.Slug == "frontend-engineering").Select(t => t.Id).FirstAsync();
    }

    private HttpRequestMessage Request(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        using var request = Request(method, url, token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }

    private async Task<JsonElement> StartAttemptAsync(Seeker seeker, int trackId)
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/assessments/tracks/{trackId}/attempts", seeker.AccessToken);
        response.EnsureSuccessStatusCode();
        return await ReadJson(response);
    }

    [Fact]
    public async Task A_seeker_can_start_an_attempt_and_gets_a_server_side_deadline()
    {
        var seeker = await NewSeekerAsync();
        var trackId = await FrontendTrackIdAsync();

        var attempt = await StartAttemptAsync(seeker, trackId);

        Assert.True(attempt.GetProperty("attemptId").GetInt64() > 0);
        Assert.True(attempt.GetProperty("expiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);
        Assert.Equal(2700, attempt.GetProperty("durationSeconds").GetInt32());
    }

    [Fact]
    public async Task The_answer_key_never_reaches_the_candidate()
    {
        var seeker = await NewSeekerAsync();
        var trackId = await FrontendTrackIdAsync();
        var attemptId = (await StartAttemptAsync(seeker, trackId)).GetProperty("attemptId").GetInt64();

        var response = await SendAsync(HttpMethod.Get, $"/api/assessments/attempts/{attemptId}", seeker.AccessToken);
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Asserted on the raw payload, not the parsed shape: the point is that
        // nothing anywhere in the response says which option is right.
        Assert.DoesNotContain("isCorrect", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correctOption", raw, StringComparison.OrdinalIgnoreCase);

        var questions = (await ReadJson(response)).GetProperty("questions");
        Assert.True(questions.GetArrayLength() > 0);
        foreach (var option in questions[0].GetProperty("options").EnumerateArray())
        {
            Assert.False(option.TryGetProperty("isCorrect", out _));
        }
    }

    [Fact]
    public async Task Answering_every_question_correctly_scores_one_hundred()
    {
        var seeker = await NewSeekerAsync();
        var trackId = await FrontendTrackIdAsync();
        var attemptId = (await StartAttemptAsync(seeker, trackId)).GetProperty("attemptId").GetInt64();

        var view = await ReadJson(
            await SendAsync(HttpMethod.Get, $"/api/assessments/attempts/{attemptId}", seeker.AccessToken));

        foreach (var question in view.GetProperty("questions").EnumerateArray())
        {
            // The seeded placeholder marks the option bodied "Correct option".
            var correct = question.GetProperty("options").EnumerateArray()
                .First(o => o.GetProperty("body").GetString() == "Correct option")
                .GetProperty("optionId").GetInt64();

            var saved = await SendAsync(
                HttpMethod.Put,
                $"/api/assessments/attempts/{attemptId}/answers/{question.GetProperty("questionId").GetInt64()}",
                seeker.AccessToken,
                new { selectedOptionId = correct, flaggedForReview = false });

            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        }

        var results = await ReadJson(
            await SendAsync(HttpMethod.Post, $"/api/assessments/attempts/{attemptId}/submit", seeker.AccessToken));

        Assert.Equal(100, results.GetProperty("overallPercent").GetInt32());
        Assert.Equal(5, results.GetProperty("sections").GetArrayLength());
        Assert.All(
            results.GetProperty("sections").EnumerateArray(),
            s => Assert.Equal(100, s.GetProperty("percent").GetInt32()));
    }

    [Fact]
    public async Task Skipping_every_question_scores_zero_rather_than_being_ignored()
    {
        var seeker = await NewSeekerAsync();
        var trackId = await FrontendTrackIdAsync();
        var attemptId = (await StartAttemptAsync(seeker, trackId)).GetProperty("attemptId").GetInt64();

        var results = await ReadJson(
            await SendAsync(HttpMethod.Post, $"/api/assessments/attempts/{attemptId}/submit", seeker.AccessToken));

        // Unanswered has to cost the same as wrong. If skipped questions were
        // dropped from the denominator, answering only the easy ones would be
        // the optimal strategy.
        Assert.Equal(0, results.GetProperty("overallPercent").GetInt32());
        Assert.Equal(5, results.GetProperty("sections").GetArrayLength());
    }

    [Fact]
    public async Task The_percentile_is_withheld_until_the_cohort_is_big_enough()
    {
        var seeker = await NewSeekerAsync();
        var trackId = await FrontendTrackIdAsync();
        var attemptId = (await StartAttemptAsync(seeker, trackId)).GetProperty("attemptId").GetInt64();

        var results = await ReadJson(
            await SendAsync(HttpMethod.Post, $"/api/assessments/attempts/{attemptId}/submit", seeker.AccessToken));

        Assert.Equal(JsonValueKind.Null, results.GetProperty("percentile").ValueKind);
    }

    [Fact]
    public async Task A_second_attempt_on_the_same_track_is_blocked_by_the_cooldown()
    {
        var seeker = await NewSeekerAsync();
        var trackId = await FrontendTrackIdAsync();
        await StartAttemptAsync(seeker, trackId);

        var second = await SendAsync(
            HttpMethod.Post, $"/api/assessments/tracks/{trackId}/attempts", seeker.AccessToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("assessment.retake_too_soon", (await ReadJson(second)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_cooldown_is_per_track_not_across_the_platform()
    {
        var seeker = await NewSeekerAsync();
        var frontend = await FrontendTrackIdAsync();
        await StartAttemptAsync(seeker, frontend);

        int otherTrack;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
            otherTrack = await db.Tracks.Where(t => t.Slug == "data-science").Select(t => t.Id).FirstAsync();
        }

        var response = await SendAsync(
            HttpMethod.Post, $"/api/assessments/tracks/{otherTrack}/attempts", seeker.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_cooldown_lifts_after_thirty_days()
    {
        var seeker = await NewSeekerAsync();
        var trackId = await FrontendTrackIdAsync();
        var attemptId = (await StartAttemptAsync(seeker, trackId)).GetProperty("attemptId").GetInt64();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
            await db.Database.ExecuteSqlAsync(
                $"UPDATE attempt SET started_at = now() - interval '31 days', expires_at = now() - interval '31 days' + interval '45 minutes', state = 3 WHERE id = {attemptId}");
        }

        var response = await SendAsync(
            HttpMethod.Post, $"/api/assessments/tracks/{trackId}/attempts", seeker.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_submission_after_the_deadline_is_rejected_by_the_server()
    {
        var seeker = await NewSeekerAsync();
        var trackId = await FrontendTrackIdAsync();
        var attemptId = (await StartAttemptAsync(seeker, trackId)).GetProperty("attemptId").GetInt64();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
            await db.Database.ExecuteSqlAsync(
                $"UPDATE attempt SET expires_at = now() - interval '1 minute' WHERE id = {attemptId}");
        }

        var submit = await SendAsync(
            HttpMethod.Post, $"/api/assessments/attempts/{attemptId}/submit", seeker.AccessToken);

        Assert.Equal(HttpStatusCode.Conflict, submit.StatusCode);
        Assert.Equal("attempt.expired", (await ReadJson(submit)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Saving_an_answer_after_the_deadline_is_rejected()
    {
        var seeker = await NewSeekerAsync();
        var trackId = await FrontendTrackIdAsync();
        var attemptId = (await StartAttemptAsync(seeker, trackId)).GetProperty("attemptId").GetInt64();

        var view = await ReadJson(
            await SendAsync(HttpMethod.Get, $"/api/assessments/attempts/{attemptId}", seeker.AccessToken));
        var question = view.GetProperty("questions")[0];

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
            await db.Database.ExecuteSqlAsync(
                $"UPDATE attempt SET expires_at = now() - interval '1 minute' WHERE id = {attemptId}");
        }

        var saved = await SendAsync(
            HttpMethod.Put,
            $"/api/assessments/attempts/{attemptId}/answers/{question.GetProperty("questionId").GetInt64()}",
            seeker.AccessToken,
            new { selectedOptionId = (long?)null, flaggedForReview = true });

        Assert.Equal(HttpStatusCode.Conflict, saved.StatusCode);
        Assert.Equal("attempt.expired", (await ReadJson(saved)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Another_seekers_attempt_reports_not_found_rather_than_forbidden()
    {
        var owner = await NewSeekerAsync();
        var intruder = await NewSeekerAsync();
        var trackId = await FrontendTrackIdAsync();
        var attemptId = (await StartAttemptAsync(owner, trackId)).GetProperty("attemptId").GetInt64();

        var read = await SendAsync(HttpMethod.Get, $"/api/assessments/attempts/{attemptId}", intruder.AccessToken);
        var submit = await SendAsync(HttpMethod.Post, $"/api/assessments/attempts/{attemptId}/submit", intruder.AccessToken);
        var results = await SendAsync(HttpMethod.Get, $"/api/assessments/attempts/{attemptId}/results", intruder.AccessToken);

        // 404 across the board. A 403 would confirm the attempt exists, which is
        // enough to enumerate other people's attempts by walking ids.
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, submit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, results.StatusCode);
    }

    [Fact]
    public async Task An_option_from_a_different_question_is_refused()
    {
        var seeker = await NewSeekerAsync();
        var trackId = await FrontendTrackIdAsync();
        var attemptId = (await StartAttemptAsync(seeker, trackId)).GetProperty("attemptId").GetInt64();

        var view = await ReadJson(
            await SendAsync(HttpMethod.Get, $"/api/assessments/attempts/{attemptId}", seeker.AccessToken));
        var questions = view.GetProperty("questions");

        var firstQuestionId = questions[0].GetProperty("questionId").GetInt64();
        var otherQuestionsOption = questions[1].GetProperty("options")[0].GetProperty("optionId").GetInt64();

        var saved = await SendAsync(
            HttpMethod.Put,
            $"/api/assessments/attempts/{attemptId}/answers/{firstQuestionId}",
            seeker.AccessToken,
            new { selectedOptionId = otherQuestionsOption, flaggedForReview = false });

        Assert.Equal(HttpStatusCode.BadRequest, saved.StatusCode);
        Assert.Equal("answer.option_invalid", (await ReadJson(saved)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Results_are_unavailable_until_the_attempt_is_submitted()
    {
        var seeker = await NewSeekerAsync();
        var trackId = await FrontendTrackIdAsync();
        var attemptId = (await StartAttemptAsync(seeker, trackId)).GetProperty("attemptId").GetInt64();

        var response = await SendAsync(
            HttpMethod.Get, $"/api/assessments/attempts/{attemptId}/results", seeker.AccessToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("attempt.not_submitted", (await ReadJson(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Submitting_twice_is_refused()
    {
        var seeker = await NewSeekerAsync();
        var trackId = await FrontendTrackIdAsync();
        var attemptId = (await StartAttemptAsync(seeker, trackId)).GetProperty("attemptId").GetInt64();

        var first = await SendAsync(HttpMethod.Post, $"/api/assessments/attempts/{attemptId}/submit", seeker.AccessToken);
        var second = await SendAsync(HttpMethod.Post, $"/api/assessments/attempts/{attemptId}/submit", seeker.AccessToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("attempt.not_in_progress", (await ReadJson(second)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_company_cannot_touch_the_assessment_endpoints()
    {
        var company = await _client.PostAsJsonAsync("/api/auth/register/company", new
        {
            companyName = $"Blocker {Guid.NewGuid():N}",
            workEmail = UniqueEmail("hr"),
            password = "Passw0rd123",
        });
        var token = (await ReadJson(company)).GetProperty("accessToken").GetString()!;
        var trackId = await FrontendTrackIdAsync();

        var response = await SendAsync(HttpMethod.Post, $"/api/assessments/tracks/{trackId}/attempts", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
