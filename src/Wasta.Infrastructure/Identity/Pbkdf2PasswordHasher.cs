using System.Security.Cryptography;
using Wasta.Application.Abstractions;

namespace Wasta.Infrastructure.Identity;

/// <summary>
/// PBKDF2-HMAC-SHA256. Argon2id would be the stronger choice but needs a
/// third-party package; PBKDF2 at OWASP's recommended iteration count is
/// defensible and ships in the BCL.
///
/// The stored format carries its own iteration count, so the cost can be raised
/// later and existing hashes keep verifying against the count they were made
/// with.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int Iterations = 210_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);

        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string hash)
    {
        var parts = hash.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        // Fixed-time compare: a byte-by-byte early exit leaks how much of the
        // hash matched, which is enough to reconstruct it given enough tries.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
