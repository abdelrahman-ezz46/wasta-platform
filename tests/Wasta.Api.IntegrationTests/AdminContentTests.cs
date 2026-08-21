using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasta.Infrastructure.Persistence;

namespace Wasta.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public class AdminContentTests(WastaApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static string Unique() => Guid.NewGuid().ToString("N")[..10];

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, string token, object? body = null, string? acceptLanguage = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (acceptLanguage is not null)
        {
            request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(acceptLanguage));
        }

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

        response.EnsureSuccessStatusCode();
        return (await ReadJson(response)).GetProperty("accessToken").GetString()!;
    }

    private static object Option(string body, bool correct, short order) =>
        new { body, isCorrect = correct, displayOrder = order };

    /// <summary>Builds a complete, publishable track through the admin API alone.</summary>
    private async Task<(int TrackId, int SectionId, int FormId, int RuleId)> BuildTrackAsync(
        string admin, int questionCount = 2)
    {
        var slug = $"built-{Unique()}";

        var trackId = (await ReadJson(await SendAsync(
                HttpMethod.Post, "/api/admin/content/tracks", admin,
                new { name = $"Built {slug}", slug, displayOrder = 99 })))
            .GetProperty("trackId").GetInt32();

        var sectionId = (await ReadJson(await SendAsync(
                HttpMethod.Post, "/api/admin/content/sections", admin,
                new { trackId, name = "Only Section", displayOrder = 0 })))
            .GetProperty("sectionId").GetInt32();

        var questionIds = new List<long>();
        for (var i = 0; i < questionCount; i++)
        {
            var id = (await ReadJson(await SendAsync(
                    HttpMethod.Post, "/api/admin/content/questions", admin,
                    new
                    {
                        trackId,
                        sectionId,
                        prompt = $"Real question {i}",
                        code = (string?)null,
                        codeLanguage = (string?)null,
                        difficulty = (short)3,
                        options = new[]
                        {
                            Option("Right", true, 0),
                            Option("Wrong", false, 1),
                        },
                    })))
                .GetProperty("questionId").GetInt64();

            questionIds.Add(id);
        }

        var formId = (await ReadJson(await SendAsync(
                HttpMethod.Post, "/api/admin/content/forms", admin,
                new { trackId, version = 1, questionCount = (short)questionCount, durationSeconds = 1800 })))
            .GetProperty("formId").GetInt32();

        var set = await SendAsync(
            HttpMethod.Put, $"/api/admin/content/forms/{formId}/questions", admin,
            new { formId, questionIds });
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        var activated = await SendAsync(
            HttpMethod.Post, $"/api/admin/content/forms/{formId}/activate", admin);
        Assert.Equal(HttpStatusCode.NoContent, activated.StatusCode);

        var ruleId = (await ReadJson(await SendAsync(
                HttpMethod.Post, "/api/admin/content/scoring-rules", admin,
                new { trackId, version = 1, notes = "Built by test" })))
            .GetProperty("ruleVersionId").GetInt32();

        await SendAsync(
            HttpMethod.Put, $"/api/admin/content/scoring-rules/{ruleId}/bands", admin,
            new
            {
                ruleVersionId = ruleId,
                bands = new[]
                {
                    new { name = "Low", minPercent = (short)0, maxPercent = (short)49 },
                    new { name = "High", minPercent = (short)50, maxPercent = (short)100 },
                },
            });

        await SendAsync(
            HttpMethod.Put, $"/api/admin/content/scoring-rules/{ruleId}/weights", admin,
            new { ruleVersionId = ruleId, weights = new Dictionary<string, decimal> { [$"{sectionId}"] = 1m } });

        var ruleActivated = await SendAsync(
            HttpMethod.Post, $"/api/admin/content/scoring-rules/{ruleId}/activate", admin);
        Assert.Equal(HttpStatusCode.NoContent, ruleActivated.StatusCode);

        await SendAsync(
            HttpMethod.Put, "/api/admin/content/tracks/" + trackId, admin,
            new { trackId, name = $"Built {slug}", isActive = true, displayOrder = 99 });

        return (trackId, sectionId, formId, ruleId);
    }

    // ---------- authorization ----------

    [Fact]
    public async Task A_company_cannot_touch_content()
    {
        var registered = await _client.PostAsJsonAsync("/api/auth/register/company", new
        {
            companyName = $"Meddler {Unique()}",
            workEmail = $"hr.{Unique()}@wasta.test",
            password = "Passw0rd123",
        });

        var token = (await ReadJson(registered)).GetProperty("accessToken").GetString()!;

        var response = await SendAsync(HttpMethod.Get, "/api/admin/content/readiness", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- question validation ----------

    [Fact]
    public async Task A_question_with_no_correct_option_is_refused()
    {
        var admin = await AdminTokenAsync();
        var slug = $"noanswer-{Unique()}";

        var trackId = (await ReadJson(await SendAsync(
                HttpMethod.Post, "/api/admin/content/tracks", admin,
                new { name = "No Answer", slug, displayOrder = 90 })))
            .GetProperty("trackId").GetInt32();

        var sectionId = (await ReadJson(await SendAsync(
                HttpMethod.Post, "/api/admin/content/sections", admin,
                new { trackId, name = "S", displayOrder = 0 })))
            .GetProperty("sectionId").GetInt32();

        var response = await SendAsync(
            HttpMethod.Post, "/api/admin/content/questions", admin,
            new
            {
                trackId,
                sectionId,
                prompt = "Which is right?",
                options = new[] { Option("A", false, 0), Option("B", false, 1) },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "question.no_correct_option", (await ReadJson(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_question_with_two_correct_options_is_refused()
    {
        var admin = await AdminTokenAsync();
        var (trackId, sectionId, _, _) = await BuildTrackAsync(admin);

        var response = await SendAsync(
            HttpMethod.Post, "/api/admin/content/questions", admin,
            new
            {
                trackId,
                sectionId,
                prompt = "Ambiguous",
                options = new[] { Option("A", true, 0), Option("B", true, 1) },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "question.multiple_correct_options", (await ReadJson(response)).GetProperty("code").GetString());
    }

    // ---------- form and scoring validation ----------

    [Fact]
    public async Task A_form_holding_the_wrong_number_of_questions_cannot_be_set()
    {
        var admin = await AdminTokenAsync();
        var (trackId, sectionId, _, _) = await BuildTrackAsync(admin);

        var questionId = (await ReadJson(await SendAsync(
                HttpMethod.Post, "/api/admin/content/questions", admin,
                new
                {
                    trackId,
                    sectionId,
                    prompt = "Spare",
                    options = new[] { Option("A", true, 0), Option("B", false, 1) },
                })))
            .GetProperty("questionId").GetInt64();

        var formId = (await ReadJson(await SendAsync(
                HttpMethod.Post, "/api/admin/content/forms", admin,
                new { trackId, version = 2, questionCount = (short)30, durationSeconds = 1800 })))
            .GetProperty("formId").GetInt32();

        var response = await SendAsync(
            HttpMethod.Put, $"/api/admin/content/forms/{formId}/questions", admin,
            new { formId, questionIds = new[] { questionId } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "form.question_count_mismatch", (await ReadJson(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Bands_with_a_gap_are_refused()
    {
        var admin = await AdminTokenAsync();
        var (trackId, _, _, _) = await BuildTrackAsync(admin);

        var ruleId = (await ReadJson(await SendAsync(
                HttpMethod.Post, "/api/admin/content/scoring-rules", admin,
                new { trackId, version = 2, notes = "gappy" })))
            .GetProperty("ruleVersionId").GetInt32();

        var response = await SendAsync(
            HttpMethod.Put, $"/api/admin/content/scoring-rules/{ruleId}/bands", admin,
            new
            {
                ruleVersionId = ruleId,
                bands = new[]
                {
                    new { name = "Low", minPercent = (short)0, maxPercent = (short)59 },
                    new { name = "High", minPercent = (short)65, maxPercent = (short)100 },
                },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ReadJson(response);
        Assert.Equal("bands.gap", body.GetProperty("code").GetString());
        Assert.Contains("60-64", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Weights_that_miss_a_section_are_refused()
    {
        var admin = await AdminTokenAsync();
        var (trackId, _, _, _) = await BuildTrackAsync(admin);

        // A second section, deliberately left out of the weights below.
        await SendAsync(
            HttpMethod.Post, "/api/admin/content/sections", admin,
            new { trackId, name = "Forgotten", displayOrder = 1 });

        var ruleId = (await ReadJson(await SendAsync(
                HttpMethod.Post, "/api/admin/content/scoring-rules", admin,
                new { trackId, version = 3, notes = "incomplete" })))
            .GetProperty("ruleVersionId").GetInt32();

        var sectionIds = new List<int>();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
            sectionIds = await db.Sections.Where(s => s.TrackId == trackId).Select(s => s.Id).ToListAsync();
        }

        var response = await SendAsync(
            HttpMethod.Put, $"/api/admin/content/scoring-rules/{ruleId}/weights", admin,
            new
            {
                ruleVersionId = ruleId,
                weights = new Dictionary<string, decimal> { [$"{sectionIds[0]}"] = 1m },
            });

        // The calculator renormalises over what it is given, so a missing
        // section would silently drop out of the score rather than fail.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "weights.section_missing", (await ReadJson(response)).GetProperty("code").GetString());
    }

    // ---------- the whole loop ----------

    [Fact]
    public async Task A_track_built_entirely_through_the_admin_api_can_be_sat_and_scored()
    {
        var admin = await AdminTokenAsync();
        var (trackId, _, _, _) = await BuildTrackAsync(admin);

        var registered = await _client.PostAsJsonAsync("/api/auth/register/seeker", new
        {
            fullName = "First Real Candidate",
            email = $"real.{Unique()}@wasta.test",
            password = "Passw0rd123",
            trackId,
        });

        var seeker = (await ReadJson(registered)).GetProperty("accessToken").GetString()!;

        var attemptId = (await ReadJson(await SendAsync(
                HttpMethod.Post, $"/api/assessments/tracks/{trackId}/attempts", seeker)))
            .GetProperty("attemptId").GetInt64();

        var view = await ReadJson(
            await SendAsync(HttpMethod.Get, $"/api/assessments/attempts/{attemptId}", seeker));

        Assert.Equal(2, view.GetProperty("questions").GetArrayLength());

        foreach (var question in view.GetProperty("questions").EnumerateArray())
        {
            var right = question.GetProperty("options").EnumerateArray()
                .First(o => o.GetProperty("body").GetString() == "Right")
                .GetProperty("optionId").GetInt64();

            await SendAsync(
                HttpMethod.Put,
                $"/api/assessments/attempts/{attemptId}/answers/{question.GetProperty("questionId").GetInt64()}",
                seeker,
                new { selectedOptionId = right, flaggedForReview = false });
        }

        var results = await ReadJson(
            await SendAsync(HttpMethod.Post, $"/api/assessments/attempts/{attemptId}/submit", seeker));

        Assert.Equal(100, results.GetProperty("overallPercent").GetInt32());
        Assert.Equal("High", results.GetProperty("sections")[0].GetProperty("bandName").GetString());
    }

    // ---------- reproducibility locks ----------

    [Fact]
    public async Task Content_used_to_score_an_attempt_becomes_immutable()
    {
        var admin = await AdminTokenAsync();
        var (trackId, sectionId, formId, ruleId) = await BuildTrackAsync(admin);

        var registered = await _client.PostAsJsonAsync("/api/auth/register/seeker", new
        {
            fullName = "Locks Content",
            email = $"lock.{Unique()}@wasta.test",
            password = "Passw0rd123",
            trackId,
        });

        var seeker = (await ReadJson(registered)).GetProperty("accessToken").GetString()!;

        var attemptId = (await ReadJson(await SendAsync(
                HttpMethod.Post, $"/api/assessments/tracks/{trackId}/attempts", seeker)))
            .GetProperty("attemptId").GetInt64();

        var view = await ReadJson(
            await SendAsync(HttpMethod.Get, $"/api/assessments/attempts/{attemptId}", seeker));

        var firstQuestionId = view.GetProperty("questions")[0].GetProperty("questionId").GetInt64();

        foreach (var question in view.GetProperty("questions").EnumerateArray())
        {
            var right = question.GetProperty("options").EnumerateArray()
                .First(o => o.GetProperty("body").GetString() == "Right")
                .GetProperty("optionId").GetInt64();

            await SendAsync(
                HttpMethod.Put,
                $"/api/assessments/attempts/{attemptId}/answers/{question.GetProperty("questionId").GetInt64()}",
                seeker,
                new { selectedOptionId = right, flaggedForReview = false });
        }

        await SendAsync(HttpMethod.Post, $"/api/assessments/attempts/{attemptId}/submit", seeker);

        // Editing any of these would change what an already-published score
        // meant. The remedy is a new version, never an edit.
        var editQuestion = await SendAsync(
            HttpMethod.Put, $"/api/admin/content/questions/{firstQuestionId}", admin,
            new
            {
                questionId = firstQuestionId,
                prompt = "Rewritten after the fact",
                options = new[] { Option("Right", true, 0), Option("Wrong", false, 1) },
            });

        var changeForm = await SendAsync(
            HttpMethod.Put, $"/api/admin/content/forms/{formId}/questions", admin,
            new { formId, questionIds = new[] { firstQuestionId } });

        var changeBands = await SendAsync(
            HttpMethod.Put, $"/api/admin/content/scoring-rules/{ruleId}/bands", admin,
            new
            {
                ruleVersionId = ruleId,
                bands = new[] { new { name = "All", minPercent = (short)0, maxPercent = (short)100 } },
            });

        Assert.Equal(HttpStatusCode.Conflict, editQuestion.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, changeForm.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, changeBands.StatusCode);
        Assert.Equal("content.locked", (await ReadJson(editQuestion)).GetProperty("code").GetString());

        // Retiring stays allowed: it removes the question from future forms
        // without touching what past attempts were scored on.
        var retired = await SendAsync(
            HttpMethod.Post, $"/api/admin/content/questions/{firstQuestionId}/retire", admin);
        Assert.Equal(HttpStatusCode.NoContent, retired.StatusCode);
    }

    [Fact]
    public async Task Publishing_a_form_retires_the_tracks_previous_one()
    {
        var admin = await AdminTokenAsync();
        var (trackId, sectionId, firstFormId, _) = await BuildTrackAsync(admin);

        var questionIds = new List<long>();
        for (var i = 0; i < 2; i++)
        {
            questionIds.Add((await ReadJson(await SendAsync(
                    HttpMethod.Post, "/api/admin/content/questions", admin,
                    new
                    {
                        trackId,
                        sectionId,
                        prompt = $"Second form question {i}",
                        options = new[] { Option("Right", true, 0), Option("Wrong", false, 1) },
                    })))
                .GetProperty("questionId").GetInt64());
        }

        var secondFormId = (await ReadJson(await SendAsync(
                HttpMethod.Post, "/api/admin/content/forms", admin,
                new { trackId, version = 5, questionCount = (short)2, durationSeconds = 1800 })))
            .GetProperty("formId").GetInt32();

        await SendAsync(
            HttpMethod.Put, $"/api/admin/content/forms/{secondFormId}/questions", admin,
            new { formId = secondFormId, questionIds });

        await SendAsync(HttpMethod.Post, $"/api/admin/content/forms/{secondFormId}/activate", admin);

        var forms = await ReadJson(
            await SendAsync(HttpMethod.Get, $"/api/admin/content/tracks/{trackId}/forms", admin));

        var active = forms.EnumerateArray().Where(f => f.GetProperty("isActive").GetBoolean()).ToList();

        // Exactly one live form per track: two would make which one a candidate
        // sits depend on ordering.
        Assert.Single(active);
        Assert.Equal(secondFormId, active[0].GetProperty("formId").GetInt32());
    }

    // ---------- readiness and translations ----------

    [Fact]
    public async Task Readiness_counts_seeded_placeholders_separately_from_real_questions()
    {
        var admin = await AdminTokenAsync();

        var readiness = await ReadJson(
            await SendAsync(HttpMethod.Get, "/api/admin/content/readiness", admin));

        var seeded = readiness.EnumerateArray()
            .First(t => t.GetProperty("trackName").GetString() == "Frontend Engineering");

        // Content that exists is not content that is real. A readiness report
        // that cannot tell the difference is worse than none.
        Assert.True(seeded.GetProperty("placeholderQuestions").GetInt32() > 0);
        Assert.Contains(
            seeded.GetProperty("blockers").EnumerateArray().Select(b => b.GetString()!),
            b => b.Contains("placeholder", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_corrected_translation_takes_effect_immediately()
    {
        var admin = await AdminTokenAsync();
        var (trackId, _, _, _) = await BuildTrackAsync(admin);

        var before = await ReadJson(
            await SendAsync(HttpMethod.Get, "/api/reference", admin, acceptLanguage: "ar"));

        var beforeName = before.GetProperty("tracks").EnumerateArray()
            .First(t => t.GetProperty("id").GetInt64() == trackId)
            .GetProperty("name").GetString();

        var arabic = $"مسار {Unique()}";
        var set = await SendAsync(
            HttpMethod.Put, "/api/admin/content/translations", admin,
            new { entityType = "track", entityId = trackId, languageTag = "ar", value = arabic });

        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        var after = await ReadJson(
            await SendAsync(HttpMethod.Get, "/api/reference", admin, acceptLanguage: "ar"));

        var afterName = after.GetProperty("tracks").EnumerateArray()
            .First(t => t.GetProperty("id").GetInt64() == trackId)
            .GetProperty("name").GetString();

        // The localizer caches a whole language. Without invalidation the
        // correction would sit unused until the process restarted.
        Assert.NotEqual(beforeName, afterName);
        Assert.Equal(arabic, afterName);
    }

    [Fact]
    public async Task An_untranslatable_entity_type_is_refused()
    {
        var admin = await AdminTokenAsync();

        var response = await SendAsync(
            HttpMethod.Put, "/api/admin/content/translations", admin,
            new { entityType = "job_post", entityId = 1, languageTag = "ar", value = "..." });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Content_changes_are_audited()
    {
        var admin = await AdminTokenAsync();
        var (trackId, _, _, _) = await BuildTrackAsync(admin);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();

        Assert.True(await db.AuditLog.AnyAsync(
            a => a.Action == "content.track_created" && a.EntityId == trackId.ToString()));
        Assert.True(await db.AuditLog.AnyAsync(a => a.Action == "content.form_activated"));
        Assert.True(await db.AuditLog.AnyAsync(a => a.Action == "content.scoring_rule_activated"));
    }
}
