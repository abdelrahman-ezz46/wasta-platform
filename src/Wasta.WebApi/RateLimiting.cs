using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Wasta.Infrastructure.Identity;

namespace Wasta.WebApi;

/// <summary>
/// Limits on the endpoints where abuse is cheap and the cost lands on us.
///
/// Behind a load balancer these depend on UseForwardedHeaders being wired,
/// which it is - without it RemoteIpAddress is the proxy and every caller in
/// the world shares one bucket.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    /// <summary>Sign-in and registration attempts per IP, per minute.</summary>
    public int AuthPerMinute { get; set; } = 10;

    /// <summary>Unlocks per company, per minute.</summary>
    public int UnlockPerMinute { get; set; } = 30;

    /// <summary>Uploads per user, per five minutes.</summary>
    public int UploadPerFiveMinutes { get; set; } = 20;
}

public static class RateLimiting
{
    public const string AuthPolicy = "auth";
    public const string UnlockPolicy = "unlock";
    public const string UploadPolicy = "upload";

    public static IServiceCollection AddWastaRateLimiting(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Configurable so a deployment can tighten these without a rebuild, and
        // so tests can raise the ones that would otherwise throttle the suite
        // itself - every test runs from one address.
        var limits = configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>()
            ?? new RateLimitOptions();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Credential stuffing is the threat here, so this is partitioned by
            // IP and deliberately tight. Legitimate users do not sign in twenty
            // times a minute.
            options.AddPolicy(AuthPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                ClientIp(http),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.AuthPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            // Partitioned by company, not IP: the limit protects a company's own
            // credit balance from a runaway script, and a company's staff share
            // an office IP.
            options.AddPolicy(UnlockPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                CompanyKey(http),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.UnlockPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            // Uploads cost disk and scanning time, so they are bounded per user.
            options.AddPolicy(UploadPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                UserKey(http),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.UploadPerFiveMinutes,
                    Window = TimeSpan.FromMinutes(5),
                    QueueLimit = 0,
                }));
        });

        return services;
    }

    private static string ClientIp(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string UserKey(HttpContext http) =>
        http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? http.User.FindFirst("sub")?.Value
        ?? ClientIp(http);

    private static string CompanyKey(HttpContext http) =>
        http.User.FindFirst(JwtTokenService.CompanyIdClaim)?.Value ?? ClientIp(http);
}
