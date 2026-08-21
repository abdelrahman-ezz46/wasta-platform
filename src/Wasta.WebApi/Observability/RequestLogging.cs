using System.Diagnostics;

namespace Wasta.WebApi.Observability;

/// <summary>
/// Gives every request an id, echoes it back, and logs one line when it
/// finishes.
///
/// The id is accepted from the caller when supplied so a trace can be followed
/// across a gateway, and generated otherwise. It goes into the logging scope, so
/// every line a request produces carries it without anything having to pass it
/// around.
/// </summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>
    /// Query parameters whose values must never reach a log.
    ///
    /// The file download route carries a signed token in the query string, and
    /// that token is the entire authorisation for the file. Logging the raw
    /// query would hand anyone with log access a working download link for
    /// every CV that was fetched.
    /// </summary>
    private static readonly string[] SensitiveQueryKeys = ["token", "access_token", "code", "password"];

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var supplied)
            && !string.IsNullOrWhiteSpace(supplied)
                ? supplied.ToString()[..Math.Min(supplied.ToString().Length, 64)]
                : context.TraceIdentifier;

        context.Response.Headers[HeaderName] = correlationId;

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
        });

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();

            // Deliberately not logging headers or bodies. Authorization carries
            // a bearer token and request bodies carry passwords; there is no
            // safe way to log either by default.
            logger.Log(
                context.Response.StatusCode >= 500 ? LogLevel.Error
                    : context.Response.StatusCode >= 400 ? LogLevel.Warning
                    : LogLevel.Information,
                "{Method} {Path} responded {StatusCode} in {ElapsedMs}ms",
                context.Request.Method,
                RedactedPath(context.Request),
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>Public so it can be tested directly; redaction is not something to verify by eye.</summary>
    public static string RedactedPath(HttpRequest request)
    {
        if (!request.QueryString.HasValue)
        {
            return request.Path;
        }

        var parts = request.Query
            .Select(pair => SensitiveQueryKeys.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)
                ? $"{pair.Key}=[redacted]"
                : $"{pair.Key}={pair.Value}")
            .ToList();

        return parts.Count == 0 ? request.Path : $"{request.Path}?{string.Join('&', parts)}";
    }
}

public static class RequestLoggingExtensions
{
    public static IApplicationBuilder UseWastaRequestLogging(this IApplicationBuilder app) =>
        app.UseMiddleware<RequestLoggingMiddleware>();
}
