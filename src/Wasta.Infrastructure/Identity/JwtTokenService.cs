using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Wasta.Application.Abstractions;
using Wasta.Domain.Identity;

namespace Wasta.Infrastructure.Identity;

public sealed class JwtTokenService : ITokenService
{
    /// <summary>Claim names the authorization handlers read to establish ownership.</summary>
    public const string SeekerIdClaim = "wasta:seeker_id";

    public const string CompanyIdClaim = "wasta:company_id";

    private readonly JwtOptions _options;
    private readonly SigningCredentials _credentials;
    private readonly IClock _clock;

    public JwtTokenService(IOptions<JwtOptions> options, IClock clock)
    {
        _options = options.Value;
        _clock = clock;

        if (string.IsNullOrWhiteSpace(_options.SigningKey) || _options.SigningKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be set and at least 32 characters. Supply it through configuration "
                + "or the Jwt__SigningKey environment variable; never commit it.");
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(_options.AccessTokenMinutes);

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_options.RefreshTokenDays);

    public string CreateAccessToken(UserAccount user, long? seekerId, long? companyId)
    {
        var now = _clock.UtcNow;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.Role, user.Role.ToString()),
        };

        // The actor id travels in the token so ownership checks do not need a
        // database round trip just to learn who is calling. The check that the
        // resource actually belongs to them still hits the database.
        if (seekerId is not null)
        {
            claims.Add(new Claim(SeekerIdClaim, seekerId.Value.ToString()));
        }

        if (companyId is not null)
        {
            claims.Add(new Claim(CompanyIdClaim, companyId.Value.ToString()));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.Add(AccessTokenLifetime).UtcDateTime,
            SigningCredentials = _credentials,
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public (string Raw, string Hash) CreateRefreshToken()
    {
        // 256 bits of entropy. Because the value is random rather than
        // user-chosen, a plain SHA-256 is the right store - there is nothing to
        // brute-force, so a slow KDF would only cost latency on every refresh.
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return (raw, HashRefreshToken(raw));
    }

    public string HashRefreshToken(string raw) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    public (string Raw, string Hash) CreateOpaqueToken()
    {
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return (raw, HashOpaqueToken(raw));
    }

    public string HashOpaqueToken(string raw) => HashRefreshToken(raw);
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
