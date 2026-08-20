using System.Security.Claims;
using Wasta.Infrastructure.Identity;

namespace Wasta.WebApi.Auth;

/// <summary>
/// Reads the actor id the token carries. Endpoints take the id from here and
/// never from the route, so a caller cannot act as someone else by editing a
/// URL. Whether the resource actually belongs to that actor is still checked
/// against the database in the handler.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static long? SeekerId(this ClaimsPrincipal user) =>
        long.TryParse(user.FindFirst(JwtTokenService.SeekerIdClaim)?.Value, out var id) ? id : null;

    public static long? CompanyId(this ClaimsPrincipal user) =>
        long.TryParse(user.FindFirst(JwtTokenService.CompanyIdClaim)?.Value, out var id) ? id : null;

    /// <summary>The account id behind the actor, recorded as the actor on ledger and audit rows.</summary>
    public static long? UserId(this ClaimsPrincipal user) =>
        long.TryParse(
            user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst("sub")?.Value,
            out var id)
            ? id
            : null;
}
