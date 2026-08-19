using Wasta.Application.Abstractions;
using Wasta.Domain.Identity;

namespace Wasta.Application.Features.Auth;

/// <summary>
/// Shared token-issuing step. Registration, login, and refresh all end the same
/// way, and duplicating it is how the three paths drift apart.
/// </summary>
internal static class AuthTokenIssuer
{
    public static async Task<AuthResult> IssueAsync(
        UserAccount user,
        long? seekerId,
        long? companyId,
        ITokenService tokens,
        IRefreshTokenRepository refreshTokens,
        IUnitOfWork unitOfWork,
        DateTimeOffset now,
        CancellationToken ct,
        long? familyId = null)
    {
        var access = tokens.CreateAccessToken(user, seekerId, companyId);
        var (raw, hash) = tokens.CreateRefreshToken();

        var refresh = new RefreshToken(user.Id, hash, now.Add(tokens.RefreshTokenLifetime), now);
        refreshTokens.Add(refresh);
        await unitOfWork.SaveChangesAsync(ct);

        // A new chain starts with the family pointing at itself, so every token
        // in a rotation lineage shares one id and can be revoked together.
        refresh.AssignFamily(familyId ?? refresh.Id);
        await unitOfWork.SaveChangesAsync(ct);

        return new AuthResult(
            access,
            raw,
            now.Add(tokens.AccessTokenLifetime),
            user.Role.ToString(),
            user.Id,
            seekerId,
            companyId);
    }
}
