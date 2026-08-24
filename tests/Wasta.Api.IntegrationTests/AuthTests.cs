using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Wasta.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public class AuthTests(WastaApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static string UniqueEmail(string prefix) => $"{prefix}.{Guid.NewGuid():N}@wasta.test";

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<JsonElement> RegisterSeekerAsync(string? email = null, string password = "Passw0rd123")
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register/seeker", new
        {
            fullName = "Test Seeker",
            email = email ?? UniqueEmail("seeker"),
            password,
        });

        response.EnsureSuccessStatusCode();
        return await ReadJson(response);
    }

    /// <summary>
    /// Sign-in is gated on a confirmed address, so any test that logs in has to
    /// confirm first - exactly as a real student would.
    ///
    /// Deliberately goes through the ANONYMOUS resend: before a successful login
    /// there is no access token, so the authenticated request endpoint is out of
    /// reach. Every test that calls this also proves that route back works.
    /// </summary>
    private async Task ConfirmEmailAsync(string email)
    {
        var resend = await _client.PostAsJsonAsync("/api/auth/verify-email/resend", new { email });
        Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);

        var token = CapturingNotificationSender.TokenFrom(factory.Sender.LastTo(email));
        Assert.False(string.IsNullOrWhiteSpace(token));

        var confirm = await _client.PostAsJsonAsync("/api/auth/verify-email/confirm", new { token });
        Assert.True(confirm.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Registering_a_seeker_returns_tokens_and_a_seeker_id()
    {
        var body = await RegisterSeekerAsync();

        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("refreshToken").GetString()));
        Assert.Equal("Seeker", body.GetProperty("role").GetString());
        Assert.True(body.GetProperty("seekerId").GetInt64() > 0);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("companyId").ValueKind);
    }

    [Fact]
    public async Task Registering_the_same_email_twice_conflicts()
    {
        var email = UniqueEmail("dupe");
        await RegisterSeekerAsync(email);

        var second = await _client.PostAsJsonAsync("/api/auth/register/seeker", new
        {
            fullName = "Someone Else",
            email,
            password = "Passw0rd123",
        });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("auth.email_taken", (await ReadJson(second)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Email_uniqueness_ignores_casing()
    {
        var email = UniqueEmail("Casing");
        await RegisterSeekerAsync(email.ToLowerInvariant());

        var second = await _client.PostAsJsonAsync("/api/auth/register/seeker", new
        {
            fullName = "Upper Case",
            email = email.ToUpperInvariant(),
            password = "Passw0rd123",
        });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Theory]
    [InlineData("short1")]
    [InlineData("nodigitshere")]
    [InlineData("12345678")]
    public async Task Weak_passwords_are_rejected(string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register/seeker", new
        {
            fullName = "Weak Password",
            email = UniqueEmail("weak"),
            password,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_succeeds_with_the_right_password()
    {
        var email = UniqueEmail("login");
        await RegisterSeekerAsync(email);
        await ConfirmEmailAsync(email);

        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd123" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Seeker", (await ReadJson(response)).GetProperty("role").GetString());
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_email_are_indistinguishable()
    {
        var email = UniqueEmail("enumeration");
        await RegisterSeekerAsync(email);

        var wrongPassword = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "WrongPass123" });
        var unknownEmail = await _client.PostAsJsonAsync(
            "/api/auth/login", new { email = UniqueEmail("ghost"), password = "WrongPass123" });

        // Same status, same code, same message. Anything that differs between
        // these two lets an attacker enumerate registered addresses.
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);

        var a = await ReadJson(wrongPassword);
        var b = await ReadJson(unknownEmail);
        Assert.Equal(a.GetProperty("code").GetString(), b.GetProperty("code").GetString());
        Assert.Equal(a.GetProperty("detail").GetString(), b.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Refreshing_rotates_the_token()
    {
        var registered = await RegisterSeekerAsync();
        var original = registered.GetProperty("refreshToken").GetString();

        var response = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = original });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(original, (await ReadJson(response)).GetProperty("refreshToken").GetString());
    }

    [Fact]
    public async Task Reusing_a_spent_refresh_token_kills_the_whole_family()
    {
        var registered = await RegisterSeekerAsync();
        var first = registered.GetProperty("refreshToken").GetString();

        var rotated = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = first });
        var second = (await ReadJson(rotated)).GetProperty("refreshToken").GetString();

        // Replaying the spent token is the signal that it leaked.
        var replay = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = first });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal("auth.refresh_reused", (await ReadJson(replay)).GetProperty("code").GetString());

        // And the successor the legitimate client holds must also be dead -
        // revoking only the replayed token would leave the attacker's copy live.
        var successor = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = second });
        Assert.Equal(HttpStatusCode.Unauthorized, successor.StatusCode);
    }

    [Fact]
    public async Task An_unknown_refresh_token_is_rejected()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "not-a-real-token" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoints_reject_an_anonymous_caller()
    {
        var response = await _client.GetAsync("/api/seekers/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_seeker_can_read_their_own_summary()
    {
        var registered = await RegisterSeekerAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/seekers/me");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", registered.GetProperty("accessToken").GetString());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            registered.GetProperty("seekerId").GetInt64(),
            (await ReadJson(response)).GetProperty("seekerId").GetInt64());
    }
}
