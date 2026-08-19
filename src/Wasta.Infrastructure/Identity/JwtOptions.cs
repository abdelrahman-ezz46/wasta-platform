namespace Wasta.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "wasta";

    public string Audience { get; set; } = "wasta-api";

    /// <summary>
    /// Signing key. Supplied by configuration or an environment variable and
    /// never committed; the API refuses to start if it is missing or too short.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 30;
}
