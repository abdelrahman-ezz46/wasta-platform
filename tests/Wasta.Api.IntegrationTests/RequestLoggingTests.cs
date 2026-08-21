using Microsoft.AspNetCore.Http;
using Wasta.WebApi.Observability;

namespace Wasta.Api.IntegrationTests;

/// <summary>
/// Redaction is the one piece of logging that has to be right. A signed file
/// URL is the entire authorisation for that file, so a raw query string in a
/// log is a working download link for every CV that was fetched.
/// </summary>
public class RequestLoggingTests
{
    private static HttpRequest RequestFor(string path, string query)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        return context.Request;
    }

    [Fact]
    public void A_path_with_no_query_is_unchanged()
    {
        Assert.Equal("/api/health/live", RequestLoggingMiddleware.RedactedPath(
            RequestFor("/api/health/live", string.Empty)));
    }

    [Fact]
    public void A_file_download_token_is_redacted()
    {
        var redacted = RequestLoggingMiddleware.RedactedPath(
            RequestFor("/api/files/cv/2026/08/abc", "?token=SIGNEDVALUE123"));

        Assert.DoesNotContain("SIGNEDVALUE123", redacted);
        Assert.Contains("token=[redacted]", redacted);
    }

    [Fact]
    public void Harmless_parameters_survive_alongside_a_redacted_one()
    {
        // Redacting everything would make the logs useless for debugging; the
        // point is to drop the credential, not the context.
        var redacted = RequestLoggingMiddleware.RedactedPath(
            RequestFor("/api/files/x", "?token=SECRET&lang=ar&page=2"));

        Assert.Contains("token=[redacted]", redacted);
        Assert.Contains("lang=ar", redacted);
        Assert.Contains("page=2", redacted);
    }

    [Theory]
    [InlineData("access_token")]
    [InlineData("code")]
    [InlineData("password")]
    [InlineData("TOKEN")]
    public void Every_sensitive_key_is_redacted_regardless_of_casing(string key)
    {
        var redacted = RequestLoggingMiddleware.RedactedPath(
            RequestFor("/api/x", $"?{key}=LEAKME"));

        Assert.DoesNotContain("LEAKME", redacted);
    }
}
