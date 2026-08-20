using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasta.Application.Features.Notifications;
using Wasta.Domain.Localization;
using Wasta.Infrastructure.Persistence;

namespace Wasta.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public class LocalizationTests(WastaApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static string UniqueEmail(string prefix) => $"{prefix}.{Guid.NewGuid():N}@wasta.test";

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<JsonElement> GetReferenceAsync(string? acceptLanguage = null, string? query = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/reference{query}");
        if (acceptLanguage is not null)
        {
            request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(acceptLanguage));
        }

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await ReadJson(response);
    }

    private static string TrackNamed(JsonElement reference, string slugHint) =>
        reference.GetProperty("tracks").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()!)
            .First(n => n.Contains(slugHint, StringComparison.OrdinalIgnoreCase)
                        || n.Contains("هندسة", StringComparison.Ordinal));

    [Fact]
    public async Task Reference_data_is_available_without_signing_in()
    {
        // The sign-up form needs the track list before anyone has an account.
        var reference = await GetReferenceAsync();

        Assert.Equal("en", reference.GetProperty("language").GetString());
        Assert.Equal(6, reference.GetProperty("tracks").GetArrayLength());
        Assert.NotEmpty(reference.GetProperty("applicationStatuses").EnumerateArray());
        Assert.NotEmpty(reference.GetProperty("locations").EnumerateArray());
    }

    [Fact]
    public async Task Accept_language_arabic_returns_arabic_reference_data()
    {
        var arabic = await GetReferenceAsync("ar");

        Assert.Equal("ar", arabic.GetProperty("language").GetString());

        var trackNames = arabic.GetProperty("tracks").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()!).ToList();

        Assert.Contains("هندسة الواجهات الأمامية", trackNames);
        Assert.DoesNotContain("Frontend Engineering", trackNames);

        var cities = arabic.GetProperty("locations").EnumerateArray()
            .Select(l => l.GetProperty("city").GetString()!).ToList();

        Assert.Contains("القاهرة", cities);
    }

    [Fact]
    public async Task A_regional_tag_resolves_to_its_primary_language()
    {
        // A browser sending ar-EG is still an Arabic speaker.
        var reference = await GetReferenceAsync("ar-EG");

        Assert.Equal("ar", reference.GetProperty("language").GetString());
    }

    [Fact]
    public async Task An_unsupported_language_falls_back_to_english()
    {
        var reference = await GetReferenceAsync("fr-FR");

        // Falling back beats failing: a browser sending something unexpected
        // should still get a usable response.
        Assert.Equal("en", reference.GetProperty("language").GetString());
    }

    [Fact]
    public async Task An_explicit_lang_parameter_beats_the_header()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/reference?lang=ar");
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));

        var reference = await ReadJson(await _client.SendAsync(request));

        Assert.Equal("ar", reference.GetProperty("language").GetString());
    }

    [Fact]
    public async Task Skills_are_not_translated()
    {
        var arabic = await GetReferenceAsync("ar");

        var skills = arabic.GetProperty("skills").EnumerateArray()
            .Select(s => s.GetProperty("name").GetString()!).ToList();

        // React and TypeScript are proper nouns. Transliterating them would make
        // them harder to recognise, not easier.
        Assert.Contains("React", skills);
        Assert.Contains("TypeScript", skills);
    }

    [Fact]
    public async Task Results_come_back_in_arabic_when_asked_for()
    {
        var registered = await _client.PostAsJsonAsync("/api/auth/register/seeker", new
        {
            fullName = "Arabic Reader",
            email = UniqueEmail("ar"),
            password = "Passw0rd123",
        });

        var body = await ReadJson(registered);
        var token = body.GetProperty("accessToken").GetString()!;

        int trackId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
            trackId = await db.Tracks.Where(t => t.Slug == "frontend-engineering")
                .Select(t => t.Id).FirstAsync();
        }

        async Task<HttpResponseMessage> Send(HttpMethod method, string url, object? payload = null)
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("ar"));
            if (payload is not null)
            {
                request.Content = JsonContent.Create(payload);
            }

            return await _client.SendAsync(request);
        }

        var attemptId = (await ReadJson(
                await Send(HttpMethod.Post, $"/api/assessments/tracks/{trackId}/attempts")))
            .GetProperty("attemptId").GetInt64();

        var view = await ReadJson(await Send(HttpMethod.Get, $"/api/assessments/attempts/{attemptId}"));
        foreach (var question in view.GetProperty("questions").EnumerateArray())
        {
            var correct = question.GetProperty("options").EnumerateArray()
                .First(o => o.GetProperty("body").GetString() == "Correct option")
                .GetProperty("optionId").GetInt64();

            await Send(
                HttpMethod.Put,
                $"/api/assessments/attempts/{attemptId}/answers/{question.GetProperty("questionId").GetInt64()}",
                new { selectedOptionId = correct, flaggedForReview = false });
        }

        var results = await ReadJson(await Send(HttpMethod.Post, $"/api/assessments/attempts/{attemptId}/submit"));
        var sections = results.GetProperty("sections").EnumerateArray().ToList();

        Assert.Contains(sections, s => s.GetProperty("sectionName").GetString() == "الأساسيات");
        Assert.Contains(sections, s => s.GetProperty("bandName").GetString() == "متميز");
    }

    [Fact]
    public async Task A_language_preference_can_be_saved_and_an_unsupported_one_is_refused()
    {
        var registered = await _client.PostAsJsonAsync("/api/auth/register/seeker", new
        {
            fullName = "Preference Setter",
            email = UniqueEmail("pref"),
            password = "Passw0rd123",
        });

        var body = await ReadJson(registered);
        var token = body.GetProperty("accessToken").GetString()!;
        var userId = body.GetProperty("userId").GetInt64();

        async Task<HttpResponseMessage> SetLanguage(string language)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, "/api/me/language")
            {
                Content = JsonContent.Create(new { language }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await _client.SendAsync(request);
        }

        var saved = await SetLanguage("ar");
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("ar", (await ReadJson(saved)).GetProperty("language").GetString());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
            var user = await db.UserAccounts.AsNoTracking().FirstAsync(u => u.Id == userId);
            Assert.Equal(Language.Arabic, user.Language);
        }

        // Rejected rather than silently stored as English - storing the wrong
        // preference quietly is worse than saying the value was not understood.
        var refused = await SetLanguage("klingon");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("language.not_supported", (await ReadJson(refused)).GetProperty("code").GetString());
    }

    [Fact]
    public void Notifications_render_in_the_recipients_language()
    {
        var payload = """{"companyName":"Nile Tech","companyId":1}""";

        var (englishSubject, englishBody) = NotificationRenderer.Render(
            NotificationKinds.ProfileUnlocked, payload, Language.English);
        var (arabicSubject, arabicBody) = NotificationRenderer.Render(
            NotificationKinds.ProfileUnlocked, payload, Language.Arabic);

        Assert.Equal("A company viewed your profile", englishSubject);
        Assert.Contains("Nile Tech", englishBody);

        Assert.Equal("شركة اطّلعت على ملفك", arabicSubject);

        // The company's own name is data, not prose, so it survives translation
        // untouched.
        Assert.Contains("Nile Tech", arabicBody);
    }

    [Fact]
    public async Task An_untranslated_row_falls_back_rather_than_disappearing()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
            db.Skills.Add(new Domain.Catalog.Skill { Name = $"Untranslated {Guid.NewGuid():N}" });
            await db.SaveChangesAsync();
        }

        var arabic = await GetReferenceAsync("ar");

        // A partially translated database stays usable: a row with no Arabic
        // name shows in English rather than vanishing from the list.
        Assert.Contains(
            arabic.GetProperty("skills").EnumerateArray(),
            s => s.GetProperty("name").GetString()!.StartsWith("Untranslated", StringComparison.Ordinal));
    }
}
