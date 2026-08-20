using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasta.Domain.Credits;
using Wasta.Infrastructure.Persistence;

namespace Wasta.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public class TalentPoolAndCreditsTests(WastaApiFactory factory)
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

        response.EnsureSuccessStatusCode();
        return (await ReadJson(response)).GetProperty("accessToken").GetString()!;
    }

    private async Task<int> TrackIdAsync(string slug)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
        return await db.Tracks.Where(t => t.Slug == slug).Select(t => t.Id).FirstAsync();
    }

    private async Task<(long CompanyId, string Token)> NewCompanyAsync(bool approve)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register/company", new
        {
            companyName = $"Hirer {Guid.NewGuid():N}",
            workEmail = UniqueEmail("hr"),
            password = "Passw0rd123",
        });

        response.EnsureSuccessStatusCode();
        var body = await ReadJson(response);
        var companyId = body.GetProperty("companyId").GetInt64();

        if (approve)
        {
            var admin = await AdminTokenAsync();
            var approved = await SendAsync(
                HttpMethod.Post, $"/api/admin/companies/{companyId}/approve", admin);
            approved.EnsureSuccessStatusCode();
        }

        return (companyId, body.GetProperty("accessToken").GetString()!);
    }

    /// <summary>A seeker with a submitted, scored attempt, so they appear in the pool.</summary>
    private async Task<(long SeekerId, string Name)> NewScoredSeekerAsync(int trackId)
    {
        var name = $"Candidate {Guid.NewGuid():N}";
        var registered = await _client.PostAsJsonAsync("/api/auth/register/seeker", new
        {
            fullName = name,
            email = UniqueEmail("seeker"),
            password = "Passw0rd123",
            trackId,
        });

        registered.EnsureSuccessStatusCode();
        var body = await ReadJson(registered);
        var token = body.GetProperty("accessToken").GetString()!;
        var seekerId = body.GetProperty("seekerId").GetInt64();

        var attemptId = (await ReadJson(
                await SendAsync(HttpMethod.Post, $"/api/assessments/tracks/{trackId}/attempts", token)))
            .GetProperty("attemptId").GetInt64();

        var view = await ReadJson(await SendAsync(HttpMethod.Get, $"/api/assessments/attempts/{attemptId}", token));
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
        return (seekerId, name);
    }

    private async Task<int> BalanceAsync(long companyId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
        return await db.CreditLedgerEntries.Where(e => e.CompanyId == companyId)
            .SumAsync(e => (int?)e.Delta) ?? 0;
    }

    private async Task<int> UnlockDebitCountAsync(long companyId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
        return await db.CreditLedgerEntries
            .CountAsync(e => e.CompanyId == companyId && e.Reason == CreditReason.Unlock);
    }

    // ---------- verification and trial credits ----------

    [Fact]
    public async Task Approving_a_company_grants_exactly_three_trial_credits()
    {
        var (companyId, _) = await NewCompanyAsync(approve: true);

        Assert.Equal(3, await BalanceAsync(companyId));
    }

    [Fact]
    public async Task Approving_twice_is_refused_and_does_not_grant_again()
    {
        var (companyId, _) = await NewCompanyAsync(approve: true);
        var admin = await AdminTokenAsync();

        var second = await SendAsync(HttpMethod.Post, $"/api/admin/companies/{companyId}/approve", admin);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(3, await BalanceAsync(companyId));
    }

    [Fact]
    public async Task A_company_cannot_reach_the_admin_endpoints()
    {
        var (_, token) = await NewCompanyAsync(approve: true);

        var response = await SendAsync(HttpMethod.Get, "/api/admin/companies/pending", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- the pool ----------

    [Fact]
    public async Task An_unverified_company_cannot_see_the_talent_pool()
    {
        var (_, token) = await NewCompanyAsync(approve: false);

        var response = await SendAsync(HttpMethod.Get, "/api/talent-pool", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Candidates_appear_anonymised_with_their_score()
    {
        var trackId = await TrackIdAsync("frontend-engineering");
        var (seekerId, name) = await NewScoredSeekerAsync(trackId);
        var (_, token) = await NewCompanyAsync(approve: true);

        var response = await SendAsync(HttpMethod.Get, $"/api/talent-pool?trackId={trackId}&pageSize=100", token);
        var raw = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(raw).RootElement;

        var candidate = body.GetProperty("items").EnumerateArray()
            .First(c => c.GetProperty("seekerId").GetInt64() == seekerId);

        Assert.Equal(100, candidate.GetProperty("overallPercent").GetInt32());
        Assert.StartsWith("#", candidate.GetProperty("candidateReference").GetString());
        Assert.False(candidate.GetProperty("isUnlocked").GetBoolean());
        Assert.DoesNotContain(name, raw);
    }

    [Fact]
    public async Task A_candidates_identity_is_hidden_until_unlocked_and_present_after()
    {
        var trackId = await TrackIdAsync("backend-engineering");
        var (seekerId, name) = await NewScoredSeekerAsync(trackId);
        var (_, token) = await NewCompanyAsync(approve: true);

        var before = await SendAsync(HttpMethod.Get, $"/api/talent-pool/{seekerId}", token);
        var beforeRaw = await before.Content.ReadAsStringAsync();
        var beforeBody = JsonDocument.Parse(beforeRaw).RootElement;

        Assert.Equal(JsonValueKind.Null, beforeBody.GetProperty("fullName").ValueKind);
        Assert.Equal(JsonValueKind.Null, beforeBody.GetProperty("email").ValueKind);
        Assert.DoesNotContain(name, beforeRaw);

        // The sections and projects are visible unlocked or not - that is what
        // the company is judging. Only the identity costs a credit.
        Assert.True(beforeBody.GetProperty("sections").GetArrayLength() > 0);

        var unlocked = await SendAsync(HttpMethod.Post, $"/api/talent-pool/{seekerId}/unlock", token);
        Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);

        var after = await ReadJson(await SendAsync(HttpMethod.Get, $"/api/talent-pool/{seekerId}", token));
        Assert.Equal(name, after.GetProperty("fullName").GetString());
        Assert.True(after.GetProperty("isUnlocked").GetBoolean());
    }

    [Fact]
    public async Task A_seeker_who_opted_out_is_not_in_the_pool_and_cannot_be_unlocked()
    {
        var trackId = await TrackIdAsync("devops");
        var (seekerId, _) = await NewScoredSeekerAsync(trackId);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
            var profile = await db.JobSeekerProfiles.FirstAsync(p => p.JobSeekerId == seekerId);
            profile.SetVisibility(false, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        var (companyId, token) = await NewCompanyAsync(approve: true);

        var detail = await SendAsync(HttpMethod.Get, $"/api/talent-pool/{seekerId}", token);
        var unlock = await SendAsync(HttpMethod.Post, $"/api/talent-pool/{seekerId}/unlock", token);

        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unlock.StatusCode);
        Assert.Equal(3, await BalanceAsync(companyId));
    }

    // ---------- spending ----------

    [Fact]
    public async Task Unlocking_spends_exactly_one_credit_and_records_it_in_the_ledger()
    {
        var trackId = await TrackIdAsync("data-science");
        var (seekerId, _) = await NewScoredSeekerAsync(trackId);
        var (companyId, token) = await NewCompanyAsync(approve: true);

        var response = await SendAsync(HttpMethod.Post, $"/api/talent-pool/{seekerId}/unlock", token);
        var body = await ReadJson(response);

        Assert.Equal("Unlocked", body.GetProperty("outcome").GetString());
        Assert.Equal(2, body.GetProperty("balanceAfter").GetInt32());
        Assert.Equal(2, await BalanceAsync(companyId));

        var ledger = await ReadJson(
            await SendAsync(HttpMethod.Get, "/api/companies/me/credits/ledger", token));
        var entries = ledger.GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(entries, e => e.GetProperty("reason").GetString() == "Unlock"
                                      && e.GetProperty("delta").GetInt32() == -1);
        Assert.Contains(entries, e => e.GetProperty("reason").GetString() == "TrialGrant"
                                      && e.GetProperty("delta").GetInt32() == 3);
    }

    [Fact]
    public async Task Unlocking_the_same_candidate_twice_charges_once()
    {
        var trackId = await TrackIdAsync("ui-ux-design");
        var (seekerId, _) = await NewScoredSeekerAsync(trackId);
        var (companyId, token) = await NewCompanyAsync(approve: true);

        var first = await ReadJson(await SendAsync(HttpMethod.Post, $"/api/talent-pool/{seekerId}/unlock", token));
        var second = await ReadJson(await SendAsync(HttpMethod.Post, $"/api/talent-pool/{seekerId}/unlock", token));

        Assert.Equal("Unlocked", first.GetProperty("outcome").GetString());

        // A retry, a double-click, or simply revisiting the profile. Success,
        // not an error, and not a second charge.
        Assert.Equal("AlreadyUnlocked", second.GetProperty("outcome").GetString());
        Assert.Equal(2, await BalanceAsync(companyId));
        Assert.Equal(1, await UnlockDebitCountAsync(companyId));
    }

    [Fact]
    public async Task Running_out_of_credits_refuses_the_unlock()
    {
        var trackId = await TrackIdAsync("product-management");
        var (companyId, token) = await NewCompanyAsync(approve: true);

        // Three trial credits, four candidates.
        var seekers = new List<long>();
        for (var i = 0; i < 4; i++)
        {
            var (seekerId, _) = await NewScoredSeekerAsync(trackId);
            seekers.Add(seekerId);
        }

        for (var i = 0; i < 3; i++)
        {
            var ok = await SendAsync(HttpMethod.Post, $"/api/talent-pool/{seekers[i]}/unlock", token);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        var broke = await SendAsync(HttpMethod.Post, $"/api/talent-pool/{seekers[3]}/unlock", token);

        Assert.Equal(HttpStatusCode.Conflict, broke.StatusCode);
        Assert.Equal("credits.insufficient", (await ReadJson(broke)).GetProperty("code").GetString());
        Assert.Equal(0, await BalanceAsync(companyId));
    }

    [Fact]
    public async Task Parallel_unlocks_of_the_same_candidate_charge_exactly_one_credit()
    {
        var trackId = await TrackIdAsync("frontend-engineering");
        var (seekerId, _) = await NewScoredSeekerAsync(trackId);
        var (companyId, token) = await NewCompanyAsync(approve: true);

        // Eight simultaneous requests for the same candidate. Without the row
        // lock and the unique index, several of these read the same balance and
        // each spend a credit for one unlock.
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            SendAsync(HttpMethod.Post, $"/api/talent-pool/{seekerId}/unlock", token)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, await UnlockDebitCountAsync(companyId));
        Assert.Equal(2, await BalanceAsync(companyId));
    }

    [Fact]
    public async Task Parallel_unlocks_cannot_overspend_the_balance()
    {
        var trackId = await TrackIdAsync("backend-engineering");
        var (companyId, token) = await NewCompanyAsync(approve: true);

        // Six different candidates, three credits, all fired at once. The
        // balance check and the spend have to be inside one lock, or several
        // requests read "3 available" before any of them writes.
        var seekers = new List<long>();
        for (var i = 0; i < 6; i++)
        {
            var (seekerId, _) = await NewScoredSeekerAsync(trackId);
            seekers.Add(seekerId);
        }

        var responses = await Task.WhenAll(seekers.Select(id =>
            SendAsync(HttpMethod.Post, $"/api/talent-pool/{id}/unlock", token)));

        var succeeded = responses.Count(r => r.StatusCode == HttpStatusCode.OK);

        Assert.Equal(3, succeeded);
        Assert.Equal(3, await UnlockDebitCountAsync(companyId));

        // The invariant that actually matters: the balance never goes negative.
        Assert.Equal(0, await BalanceAsync(companyId));
    }

    // ---------- top-ups ----------

    [Fact]
    public async Task A_top_up_request_adds_credits_only_after_an_admin_confirms_it()
    {
        var (companyId, token) = await NewCompanyAsync(approve: true);

        int paymentMethodId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
            paymentMethodId = await db.PaymentMethods.Select(p => p.Id).FirstAsync();
        }

        var requested = await SendAsync(
            HttpMethod.Post, "/api/companies/me/credits/topups", token,
            new { creditsRequested = 10, paymentMethodId, amount = 5000m, currency = "EGP" });

        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var requestId = (await ReadJson(requested)).GetProperty("requestId").GetInt64();

        // Nothing has moved yet: the money is still in transit out of band.
        Assert.Equal(3, await BalanceAsync(companyId));

        var admin = await AdminTokenAsync();
        var reviewed = await SendAsync(
            HttpMethod.Post, $"/api/admin/topups/{requestId}/review", admin,
            new { approve = true, note = "Transfer received." });

        Assert.Equal(HttpStatusCode.NoContent, reviewed.StatusCode);
        Assert.Equal(13, await BalanceAsync(companyId));
    }

    [Fact]
    public async Task A_rejected_top_up_adds_nothing()
    {
        var (companyId, token) = await NewCompanyAsync(approve: true);

        int paymentMethodId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
            paymentMethodId = await db.PaymentMethods.Select(p => p.Id).FirstAsync();
        }

        var requestId = (await ReadJson(await SendAsync(
                HttpMethod.Post, "/api/companies/me/credits/topups", token,
                new { creditsRequested = 25, paymentMethodId, amount = 1m, currency = "EGP" })))
            .GetProperty("requestId").GetInt64();

        var admin = await AdminTokenAsync();
        await SendAsync(
            HttpMethod.Post, $"/api/admin/topups/{requestId}/review", admin,
            new { approve = false, note = "No transfer found." });

        Assert.Equal(3, await BalanceAsync(companyId));

        var second = await SendAsync(
            HttpMethod.Post, $"/api/admin/topups/{requestId}/review", admin,
            new { approve = true, note = "Changed my mind." });

        // Already reviewed. Reversing a decision has to be a new request, or a
        // rejected transfer could be quietly turned into credits later.
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(3, await BalanceAsync(companyId));
    }
}
