using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wasta.Infrastructure.Persistence;

namespace Wasta.Api.IntegrationTests;

/// <summary>
/// The rules that decide who may reach what. These are the tests worth having:
/// a broken feature is visible, a broken authorization rule is silent until
/// someone reads data they should never have seen.
/// </summary>
[Collection(nameof(ApiCollection))]
public class AuthorizationTests(WastaApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static string UniqueEmail(string prefix) => $"{prefix}.{Guid.NewGuid():N}@wasta.test";

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<JsonElement> RegisterSeekerAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register/seeker", new
        {
            fullName = "Seeker",
            email = UniqueEmail("seeker"),
            password = "Passw0rd123",
        });

        response.EnsureSuccessStatusCode();
        return await ReadJson(response);
    }

    private async Task<JsonElement> RegisterCompanyAsync(string? name = null)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register/company", new
        {
            companyName = name ?? $"Company {Guid.NewGuid():N}",
            workEmail = UniqueEmail("hr"),
            password = "Passw0rd123",
        });

        response.EnsureSuccessStatusCode();
        return await ReadJson(response);
    }

    private async Task<HttpResponseMessage> GetAsync(string url, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    private async Task ApproveCompanyAsync(long companyId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
        var company = await db.Companies.FirstAsync(c => c.Id == companyId);
        company.Approve(adminUserId: 1, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_seeker_cannot_reach_a_company_endpoint()
    {
        var seeker = await RegisterSeekerAsync();

        var response = await GetAsync("/api/companies/me", seeker.GetProperty("accessToken").GetString()!);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_company_cannot_reach_a_seeker_endpoint()
    {
        var company = await RegisterCompanyAsync();

        var response = await GetAsync("/api/seekers/me", company.GetProperty("accessToken").GetString()!);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_new_company_starts_unverified()
    {
        var company = await RegisterCompanyAsync();

        var response = await GetAsync("/api/companies/me", company.GetProperty("accessToken").GetString()!);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJson(response);
        Assert.Equal("Pending", body.GetProperty("verificationState").GetString());
        Assert.False(body.GetProperty("isVerified").GetBoolean());
    }

    [Fact]
    public async Task An_unverified_company_is_locked_out_of_credits()
    {
        var company = await RegisterCompanyAsync();

        var response = await GetAsync("/api/companies/me/credits", company.GetProperty("accessToken").GetString()!);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Approval_opens_credits_to_a_token_that_was_already_issued()
    {
        var company = await RegisterCompanyAsync();
        var token = company.GetProperty("accessToken").GetString()!;
        var companyId = company.GetProperty("companyId").GetInt64();

        var before = await GetAsync("/api/companies/me/credits", token);
        Assert.Equal(HttpStatusCode.Forbidden, before.StatusCode);

        await ApproveCompanyAsync(companyId);

        // Same token, no re-login. Verification is read from the database on
        // every request, so revoking it takes effect immediately too - if it
        // were baked into the token, a revoked company would keep its access
        // until the token expired.
        var after = await GetAsync("/api/companies/me/credits", token);

        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Equal(0, (await ReadJson(after)).GetProperty("balance").GetInt32());
    }

    [Fact]
    public async Task Two_companies_cannot_register_the_same_name()
    {
        var name = $"Nile Tech {Guid.NewGuid():N}";
        await RegisterCompanyAsync(name);

        var duplicate = await _client.PostAsJsonAsync("/api/auth/register/company", new
        {
            companyName = name.ToUpperInvariant(),
            workEmail = UniqueEmail("hr"),
            password = "Passw0rd123",
        });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("company.name_taken", (await ReadJson(duplicate)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_garbage_token_is_rejected()
    {
        var response = await GetAsync("/api/seekers/me", "not.a.jwt");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
