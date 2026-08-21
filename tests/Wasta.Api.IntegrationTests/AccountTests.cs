using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasta.Infrastructure.Persistence;

namespace Wasta.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public class AccountTests(WastaApiFactory factory)
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

    private sealed record Registered(long UserId, string Email, string AccessToken, string RefreshToken);

    private async Task<Registered> RegisterAsync(string password = "Passw0rd123")
    {
        var email = UniqueEmail("account");
        var response = await _client.PostAsJsonAsync("/api/auth/register/seeker", new
        {
            fullName = "Account Holder",
            email,
            password,
        });

        response.EnsureSuccessStatusCode();
        var body = await ReadJson(response);

        return new Registered(
            body.GetProperty("userId").GetInt64(),
            email,
            body.GetProperty("accessToken").GetString()!,
            body.GetProperty("refreshToken").GetString()!);
    }

    private string? TokenSentTo(string email) =>
        CapturingNotificationSender.TokenFrom(factory.Sender.LastTo(email));

    // ---------- email verification ----------

    [Fact]
    public async Task A_new_account_starts_unverified_and_can_be_confirmed_from_the_emailed_link()
    {
        var user = await RegisterAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
            var before = await db.UserAccounts.AsNoTracking().FirstAsync(u => u.Id == user.UserId);
            Assert.Null(before.EmailVerifiedAt);
        }

        var requested = await SendAsync(HttpMethod.Post, "/api/auth/verify-email/request", user.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, requested.StatusCode);

        var token = TokenSentTo(user.Email);
        Assert.False(string.IsNullOrWhiteSpace(token));

        var confirmed = await SendAsync(
            HttpMethod.Post, "/api/auth/verify-email/confirm", body: new { token });
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
            var after = await db.UserAccounts.AsNoTracking().FirstAsync(u => u.Id == user.UserId);
            Assert.NotNull(after.EmailVerifiedAt);
        }
    }

    [Fact]
    public async Task Requesting_a_second_link_kills_the_first_one()
    {
        var user = await RegisterAsync();

        await SendAsync(HttpMethod.Post, "/api/auth/verify-email/request", user.AccessToken);
        var first = TokenSentTo(user.Email);

        await SendAsync(HttpMethod.Post, "/api/auth/verify-email/request", user.AccessToken);
        var second = TokenSentTo(user.Email);

        Assert.NotEqual(first, second);

        // Requesting twice must not leave a spare valid link sitting in an inbox.
        var stale = await SendAsync(
            HttpMethod.Post, "/api/auth/verify-email/confirm", body: new { token = first });
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);

        var fresh = await SendAsync(
            HttpMethod.Post, "/api/auth/verify-email/confirm", body: new { token = second });
        Assert.Equal(HttpStatusCode.NoContent, fresh.StatusCode);
    }

    [Fact]
    public async Task A_verification_token_is_single_use()
    {
        var user = await RegisterAsync();
        await SendAsync(HttpMethod.Post, "/api/auth/verify-email/request", user.AccessToken);
        var token = TokenSentTo(user.Email);

        await SendAsync(HttpMethod.Post, "/api/auth/verify-email/confirm", body: new { token });
        var replay = await SendAsync(
            HttpMethod.Post, "/api/auth/verify-email/confirm", body: new { token });

        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task A_made_up_token_is_refused()
    {
        var response = await SendAsync(
            HttpMethod.Post, "/api/auth/verify-email/confirm", body: new { token = "not-a-real-token" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("token.invalid", (await ReadJson(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Asking_to_verify_an_already_verified_address_is_refused()
    {
        var user = await RegisterAsync();
        await SendAsync(HttpMethod.Post, "/api/auth/verify-email/request", user.AccessToken);
        await SendAsync(
            HttpMethod.Post, "/api/auth/verify-email/confirm", body: new { token = TokenSentTo(user.Email) });

        var again = await SendAsync(HttpMethod.Post, "/api/auth/verify-email/request", user.AccessToken);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    // ---------- password reset ----------

    [Fact]
    public async Task A_reset_link_changes_the_password_and_the_old_one_stops_working()
    {
        var user = await RegisterAsync();

        var requested = await SendAsync(
            HttpMethod.Post, "/api/auth/forgot-password", body: new { email = user.Email });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);

        var token = TokenSentTo(user.Email);
        Assert.False(string.IsNullOrWhiteSpace(token));

        var reset = await SendAsync(
            HttpMethod.Post, "/api/auth/reset-password", body: new { token, newPassword = "BrandNew123" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var withOld = await _client.PostAsJsonAsync(
            "/api/auth/login", new { email = user.Email, password = "Passw0rd123" });
        var withNew = await _client.PostAsJsonAsync(
            "/api/auth/login", new { email = user.Email, password = "BrandNew123" });

        Assert.Equal(HttpStatusCode.Unauthorized, withOld.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withNew.StatusCode);
    }

    [Fact]
    public async Task A_reset_ends_every_existing_session()
    {
        var user = await RegisterAsync();

        await SendAsync(HttpMethod.Post, "/api/auth/forgot-password", body: new { email = user.Email });
        await SendAsync(
            HttpMethod.Post, "/api/auth/reset-password",
            body: new { token = TokenSentTo(user.Email), newPassword = "BrandNew123" });

        // A reset is what someone does when they think they are compromised.
        // Leaving the attacker's refresh token alive would make it theatre.
        var refreshed = await _client.PostAsJsonAsync(
            "/api/auth/refresh", new { refreshToken = user.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, refreshed.StatusCode);
    }

    [Fact]
    public async Task Forgot_password_answers_identically_for_a_stranger()
    {
        var user = await RegisterAsync();

        var known = await SendAsync(
            HttpMethod.Post, "/api/auth/forgot-password", body: new { email = user.Email });
        var unknown = await SendAsync(
            HttpMethod.Post, "/api/auth/forgot-password", body: new { email = UniqueEmail("ghost") });

        // Anything that differs here turns the endpoint into a membership
        // oracle: send an address, learn whether it has an account.
        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(
            await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task No_email_goes_to_an_address_that_has_no_account()
    {
        var stranger = UniqueEmail("nobody");

        await SendAsync(HttpMethod.Post, "/api/auth/forgot-password", body: new { email = stranger });

        // The response is identical, but nothing is actually sent - the identical
        // response must not come at the cost of mailing strangers.
        Assert.Null(factory.Sender.LastTo(stranger));
    }

    [Fact]
    public async Task A_reset_token_is_single_use()
    {
        var user = await RegisterAsync();
        await SendAsync(HttpMethod.Post, "/api/auth/forgot-password", body: new { email = user.Email });
        var token = TokenSentTo(user.Email);

        await SendAsync(
            HttpMethod.Post, "/api/auth/reset-password", body: new { token, newPassword = "FirstNew123" });

        var replay = await SendAsync(
            HttpMethod.Post, "/api/auth/reset-password", body: new { token, newPassword = "SecondNew123" });

        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task A_weak_new_password_is_refused()
    {
        var user = await RegisterAsync();
        await SendAsync(HttpMethod.Post, "/api/auth/forgot-password", body: new { email = user.Email });

        var response = await SendAsync(
            HttpMethod.Post, "/api/auth/reset-password",
            body: new { token = TokenSentTo(user.Email), newPassword = "short" });

        // The reset path enforces the same policy as registration. A rule that
        // only applies on the way in is not a rule.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- PDPL: access and erasure ----------

    [Fact]
    public async Task A_user_can_export_everything_held_about_them()
    {
        var user = await RegisterAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/me/export", user.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var export = await ReadJson(response);
        Assert.True(export.TryGetProperty("account", out _));
        Assert.True(export.TryGetProperty("seeker", out _));
        Assert.True(export.TryGetProperty("attempts", out _));
        Assert.Contains(user.Email, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_export_needs_authentication()
    {
        Assert.Equal(
            HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/me/export")).StatusCode);
    }

    [Fact]
    public async Task Erasing_an_account_scrubs_the_identity_and_ends_sign_in()
    {
        var user = await RegisterAsync();

        var deleted = await SendAsync(HttpMethod.Delete, "/api/me", user.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var signIn = await _client.PostAsJsonAsync(
            "/api/auth/login", new { email = user.Email, password = "Passw0rd123" });
        Assert.Equal(HttpStatusCode.Unauthorized, signIn.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
        var row = await db.UserAccounts.AsNoTracking().FirstAsync(u => u.Id == user.UserId);

        // The row survives so ledger and unlock foreign keys stay intact, but
        // nothing identifying is left on it.
        Assert.NotNull(row.DeletedAt);
        Assert.DoesNotContain(user.Email, row.Email);
        Assert.Equal(string.Empty, row.PasswordHash);
    }

    [Fact]
    public async Task An_erased_account_cannot_be_erased_again()
    {
        var user = await RegisterAsync();
        await SendAsync(HttpMethod.Delete, "/api/me", user.AccessToken);

        var again = await SendAsync(HttpMethod.Delete, "/api/me", user.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task Erasure_leaves_a_companys_purchase_history_intact()
    {
        int trackId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
            trackId = await db.Tracks.Where(t => t.Slug == "frontend-engineering")
                .Select(t => t.Id).FirstAsync();
        }

        // A seeker with a score, so a company can unlock them.
        var seekerEmail = UniqueEmail("erased");
        var registered = await _client.PostAsJsonAsync("/api/auth/register/seeker", new
        {
            fullName = "Soon Erased",
            email = seekerEmail,
            password = "Passw0rd123",
            trackId,
        });
        var seekerBody = await ReadJson(registered);
        var seekerToken = seekerBody.GetProperty("accessToken").GetString()!;
        var seekerId = seekerBody.GetProperty("seekerId").GetInt64();

        var attemptId = (await ReadJson(await SendAsync(
                HttpMethod.Post, $"/api/assessments/tracks/{trackId}/attempts", seekerToken)))
            .GetProperty("attemptId").GetInt64();

        var view = await ReadJson(
            await SendAsync(HttpMethod.Get, $"/api/assessments/attempts/{attemptId}", seekerToken));

        foreach (var question in view.GetProperty("questions").EnumerateArray())
        {
            var correct = question.GetProperty("options").EnumerateArray()
                .First(o => o.GetProperty("body").GetString() == "Correct option")
                .GetProperty("optionId").GetInt64();

            await SendAsync(
                HttpMethod.Put,
                $"/api/assessments/attempts/{attemptId}/answers/{question.GetProperty("questionId").GetInt64()}",
                seekerToken,
                new { selectedOptionId = correct, flaggedForReview = false });
        }

        await SendAsync(HttpMethod.Post, $"/api/assessments/attempts/{attemptId}/submit", seekerToken);

        var companyResponse = await _client.PostAsJsonAsync("/api/auth/register/company", new
        {
            companyName = $"Buyer {Guid.NewGuid():N}",
            workEmail = UniqueEmail("hr"),
            password = "Passw0rd123",
        });
        var companyBody = await ReadJson(companyResponse);
        var companyId = companyBody.GetProperty("companyId").GetInt64();
        var companyToken = companyBody.GetProperty("accessToken").GetString()!;

        var adminLogin = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = WastaApiFactory.AdminEmail,
            password = WastaApiFactory.AdminPassword,
        });
        var adminToken = (await ReadJson(adminLogin)).GetProperty("accessToken").GetString()!;
        await SendAsync(HttpMethod.Post, $"/api/admin/companies/{companyId}/approve", adminToken);

        var unlocked = await SendAsync(
            HttpMethod.Post, $"/api/talent-pool/{seekerId}/unlock", companyToken);
        Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);

        await SendAsync(HttpMethod.Delete, "/api/me", seekerToken);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();

            // The company paid for this. Erasing the person must not erase the
            // other party's financial record of the transaction.
            Assert.True(await db.ProfileUnlocks.AnyAsync(
                u => u.CompanyId == companyId && u.JobSeekerId == seekerId));
            Assert.Equal(2, await db.CreditLedgerEntries
                .Where(e => e.CompanyId == companyId).SumAsync(e => e.Delta));
        }
    }

    // ---------- signing out ----------

    [Fact]
    public async Task Logging_out_revokes_the_refresh_token()
    {
        var user = await RegisterAsync();

        var out1 = await SendAsync(
            HttpMethod.Post, "/api/auth/logout", user.AccessToken,
            new { refreshToken = user.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, out1.StatusCode);

        // Without this a user has no way to revoke their own session: clearing
        // the browser on a shared machine would leave the credential valid for
        // another thirty days.
        var refreshed = await _client.PostAsJsonAsync(
            "/api/auth/refresh", new { refreshToken = user.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, refreshed.StatusCode);
    }

    [Fact]
    public async Task Logging_out_of_all_sessions_ends_every_one_of_them()
    {
        var user = await RegisterAsync();

        // A second session, as if from another device.
        var second = await _client.PostAsJsonAsync(
            "/api/auth/login", new { email = user.Email, password = "Passw0rd123" });
        var secondRefresh = (await ReadJson(second)).GetProperty("refreshToken").GetString();

        await SendAsync(
            HttpMethod.Post, "/api/auth/logout", user.AccessToken, new { allSessions = true });

        var first = await _client.PostAsJsonAsync(
            "/api/auth/refresh", new { refreshToken = user.RefreshToken });
        var other = await _client.PostAsJsonAsync(
            "/api/auth/refresh", new { refreshToken = secondRefresh });

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);
    }

    [Fact]
    public async Task Logging_out_with_someone_elses_token_changes_nothing()
    {
        var mine = await RegisterAsync();
        var theirs = await RegisterAsync();

        var response = await SendAsync(
            HttpMethod.Post, "/api/auth/logout", mine.AccessToken,
            new { refreshToken = theirs.RefreshToken });

        // A no-op, not an error: telling a caller their guess was wrong is a
        // probe result, and their session must obviously survive.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var stillValid = await _client.PostAsJsonAsync(
            "/api/auth/refresh", new { refreshToken = theirs.RefreshToken });

        Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);
    }

    [Fact]
    public async Task Logging_out_needs_authentication()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/logout", new { allSessions = true });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Security_events_are_written_to_the_audit_log()
    {
        var user = await RegisterAsync();
        await SendAsync(HttpMethod.Post, "/api/auth/forgot-password", body: new { email = user.Email });
        await SendAsync(
            HttpMethod.Post, "/api/auth/reset-password",
            body: new { token = TokenSentTo(user.Email), newPassword = "Audited123" });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();

        Assert.True(await db.AuditLog.AnyAsync(
            a => a.ActorUserId == user.UserId && a.Action == "account.password_reset"));
    }
}
