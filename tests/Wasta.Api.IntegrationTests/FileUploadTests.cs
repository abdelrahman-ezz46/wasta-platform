using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wasta.Application.Abstractions;
using Wasta.Application.Features.Files;

namespace Wasta.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public class FileUploadTests(WastaApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static string UniqueEmail(string prefix) => $"{prefix}.{Guid.NewGuid():N}@wasta.test";

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static byte[] FakePdf(int size = 512)
    {
        var bytes = new byte[size];
        "%PDF-1.7"u8.CopyTo(bytes);
        return bytes;
    }

    private static byte[] FakeExecutable(int size = 512)
    {
        var bytes = new byte[size];
        // "MZ" - a Windows executable, whatever the extension claims.
        bytes[0] = 0x4D;
        bytes[1] = 0x5A;
        return bytes;
    }

    private async Task<string> NewSeekerTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register/seeker", new
        {
            fullName = "Uploader",
            email = UniqueEmail("upload"),
            password = "Passw0rd123",
        });

        response.EnsureSuccessStatusCode();
        return (await ReadJson(response)).GetProperty("accessToken").GetString()!;
    }

    private async Task<HttpResponseMessage> UploadAsync(
        string url, string token, byte[] content, string fileName, string contentType)
    {
        using var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(content);
        part.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(part, "file", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task A_pdf_cv_uploads_and_comes_back_through_its_signed_url()
    {
        var token = await NewSeekerTokenAsync();
        var content = FakePdf();

        var uploaded = await UploadAsync("/api/seekers/me/cv", token, content, "layla-cv.pdf", "application/pdf");
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);

        var body = await ReadJson(uploaded);
        var downloadUrl = body.GetProperty("downloadUrl").GetString()!;

        Assert.Equal("layla-cv.pdf", body.GetProperty("fileName").GetString());
        Assert.Equal(content.Length, body.GetProperty("length").GetInt64());

        // The download needs no bearer token: the signature is the authorisation.
        var downloaded = await _client.GetAsync(downloadUrl);
        Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
        Assert.Equal(content, await downloaded.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task An_executable_renamed_to_pdf_is_refused()
    {
        var token = await NewSeekerTokenAsync();

        var response = await UploadAsync(
            "/api/seekers/me/cv", token, FakeExecutable(), "cv.pdf", "application/pdf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("file.content_mismatch", (await ReadJson(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_cv_that_is_not_a_pdf_is_refused()
    {
        var token = await NewSeekerTokenAsync();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };

        var response = await UploadAsync("/api/seekers/me/cv", token, png, "cv.png", "image/png");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("file.type_not_allowed", (await ReadJson(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_oversized_cv_is_refused()
    {
        var token = await NewSeekerTokenAsync();

        var response = await UploadAsync(
            "/api/seekers/me/cv", token,
            FakePdf((int)FileValidation.MaxCvBytes + 1024), "huge.pdf", "application/pdf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("file.too_large", (await ReadJson(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_download_without_a_token_is_not_found()
    {
        var token = await NewSeekerTokenAsync();
        var uploaded = await ReadJson(
            await UploadAsync("/api/seekers/me/cv", token, FakePdf(), "cv.pdf", "application/pdf"));

        var key = uploaded.GetProperty("key").GetString();

        var response = await _client.GetAsync($"/api/files/{key}");

        // 404 rather than 401: probing for which keys exist must reveal nothing.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_tampered_token_is_rejected()
    {
        var token = await NewSeekerTokenAsync();
        var uploaded = await ReadJson(
            await UploadAsync("/api/seekers/me/cv", token, FakePdf(), "cv.pdf", "application/pdf"));

        var url = uploaded.GetProperty("downloadUrl").GetString()!;
        var tampered = url[..^2] + (url[^2] == 'a' ? "bb" : "aa");

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(tampered)).StatusCode);
    }

    [Fact]
    public async Task A_token_for_one_file_does_not_open_another()
    {
        var tokenA = await NewSeekerTokenAsync();
        var tokenB = await NewSeekerTokenAsync();

        var first = await ReadJson(
            await UploadAsync("/api/seekers/me/cv", tokenA, FakePdf(), "a.pdf", "application/pdf"));
        var second = await ReadJson(
            await UploadAsync("/api/seekers/me/cv", tokenB, FakePdf(), "b.pdf", "application/pdf"));

        var firstSignature = first.GetProperty("downloadUrl").GetString()!.Split("token=")[1];
        var secondKey = second.GetProperty("key").GetString();

        // The signature covers the key, so lifting one file's token onto
        // another's path proves nothing.
        var response = await _client.GetAsync($"/api/files/{secondKey}?token={firstSignature}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_expired_token_is_rejected()
    {
        var token = await NewSeekerTokenAsync();
        var uploaded = await ReadJson(
            await UploadAsync("/api/seekers/me/cv", token, FakePdf(), "cv.pdf", "application/pdf"));

        var key = uploaded.GetProperty("key").GetString()!;

        using var scope = factory.Services.CreateScope();
        var signer = scope.ServiceProvider.GetRequiredService<IFileUrlSigner>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var expired = signer.CreateToken(key, clock.UtcNow.AddMinutes(-1));

        Assert.Equal(
            HttpStatusCode.NotFound, (await _client.GetAsync($"/api/files/{key}?token={expired}")).StatusCode);
    }

    [Fact]
    public async Task Replacing_a_cv_removes_the_previous_file()
    {
        var token = await NewSeekerTokenAsync();

        var first = await ReadJson(
            await UploadAsync("/api/seekers/me/cv", token, FakePdf(), "old.pdf", "application/pdf"));
        var firstUrl = first.GetProperty("downloadUrl").GetString()!;

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(firstUrl)).StatusCode);

        await UploadAsync("/api/seekers/me/cv", token, FakePdf(256), "new.pdf", "application/pdf");

        // A CV is personal data. Keeping superseded copies means holding data
        // nobody asked us to keep.
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(firstUrl)).StatusCode);
    }

    [Fact]
    public async Task Uploading_a_cv_raises_profile_strength()
    {
        var token = await NewSeekerTokenAsync();

        using var before = new HttpRequestMessage(HttpMethod.Get, "/api/seekers/me");
        before.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var strengthBefore = (await ReadJson(await _client.SendAsync(before)))
            .GetProperty("profileStrength").GetInt32();

        await UploadAsync("/api/seekers/me/cv", token, FakePdf(), "cv.pdf", "application/pdf");

        using var after = new HttpRequestMessage(HttpMethod.Get, "/api/seekers/me");
        after.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var strengthAfter = (await ReadJson(await _client.SendAsync(after)))
            .GetProperty("profileStrength").GetInt32();

        Assert.True(strengthAfter > strengthBefore);
    }

    [Fact]
    public async Task Another_seekers_application_cannot_be_given_files()
    {
        var trackId = 1;
        var owner = await NewSeekerTokenAsync();
        var intruder = await NewSeekerTokenAsync();

        // A company posting, so there is something to apply to.
        var company = await _client.PostAsJsonAsync("/api/auth/register/company", new
        {
            companyName = $"Filer {Guid.NewGuid():N}",
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

        using var approve = new HttpRequestMessage(
            HttpMethod.Post, $"/api/admin/companies/{companyId}/approve");
        approve.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        await _client.SendAsync(approve);

        using var post = new HttpRequestMessage(HttpMethod.Post, "/api/companies/me/jobs")
        {
            Content = JsonContent.Create(new
            {
                title = $"Role {Guid.NewGuid():N}",
                trackId,
                jobDescription = "Work.",
            }),
        };
        post.Headers.Authorization = new AuthenticationHeaderValue("Bearer", companyToken);
        var jobId = (await ReadJson(await _client.SendAsync(post))).GetProperty("jobPostId").GetInt64();

        using var apply = new HttpRequestMessage(HttpMethod.Post, $"/api/jobs/{jobId}/apply");
        apply.Headers.Authorization = new AuthenticationHeaderValue("Bearer", owner);
        var applicationId = (await ReadJson(await _client.SendAsync(apply)))
            .GetProperty("applicationId").GetInt64();

        var response = await UploadAsync(
            $"/api/seekers/me/applications/{applicationId}/files",
            intruder, FakePdf(), "mine.pdf", "application/pdf");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Uploads_are_rate_limited_per_user()
    {
        var token = await NewSeekerTokenAsync();

        // The test host allows five per window. The sixth from this user should
        // be refused - and because the partition is per user, nobody else's
        // uploads are affected.
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 7; i++)
        {
            var response = await UploadAsync(
                "/api/seekers/me/cv", token, FakePdf(), $"cv{i}.pdf", "application/pdf");
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(5, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(2, statuses.Count(s => s == HttpStatusCode.TooManyRequests));

        // Another user is unaffected by the first one's exhausted budget.
        var other = await NewSeekerTokenAsync();
        var unaffected = await UploadAsync(
            "/api/seekers/me/cv", other, FakePdf(), "fine.pdf", "application/pdf");

        Assert.Equal(HttpStatusCode.OK, unaffected.StatusCode);
    }
}
