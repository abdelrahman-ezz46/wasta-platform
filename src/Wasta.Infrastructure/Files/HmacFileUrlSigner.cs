using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Wasta.Application.Abstractions;
using Wasta.Application.Features.Files;
using Wasta.Infrastructure.Identity;

namespace Wasta.Infrastructure.Files;

/// <summary>
/// Time-limited download tokens.
///
/// The signing key is derived from the JWT key rather than configured
/// separately: one secret to rotate, and domain separation means a token minted
/// here can never be presented as an access token.
/// </summary>
public sealed class HmacFileUrlSigner : IFileUrlSigner
{
    private readonly byte[] _key;
    private readonly IClock _clock;

    public HmacFileUrlSigner(IOptions<JwtOptions> jwt, IOptions<FileStorageOptions> files, IClock clock)
    {
        _clock = clock;
        DefaultLifetime = TimeSpan.FromMinutes(files.Value.SignedUrlMinutes);

        _key = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(jwt.Value.SigningKey),
            "wasta:file-url-signing"u8.ToArray());
    }

    public TimeSpan DefaultLifetime { get; }

    public string CreateToken(string key, DateTimeOffset expiresAt)
    {
        var expiry = expiresAt.ToUnixTimeSeconds();
        return $"{expiry}.{Base64UrlEncoder.Encode(Sign(key, expiry))}";
    }

    public bool IsValid(string key, string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 2 || !long.TryParse(parts[0], out var expiry))
        {
            return false;
        }

        if (DateTimeOffset.FromUnixTimeSeconds(expiry) < _clock.UtcNow)
        {
            return false;
        }

        byte[] presented;
        try
        {
            presented = Base64UrlEncoder.DecodeBytes(parts[1]);
        }
        catch (Exception)
        {
            return false;
        }

        // Fixed-time compare: a byte-by-byte exit would leak how much of the
        // signature matched, which is enough to forge one given enough tries.
        return CryptographicOperations.FixedTimeEquals(presented, Sign(key, expiry));
    }

    private byte[] Sign(string key, long expiry) =>
        HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{key}|{expiry}"));
}
