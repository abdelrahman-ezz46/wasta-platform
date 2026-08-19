using Microsoft.AspNetCore.Authorization;
using Wasta.Application.Abstractions;
using Wasta.Infrastructure.Identity;

namespace Wasta.WebApi.Auth;

public static class Policies
{
    public const string SeekerOnly = "SeekerOnly";
    public const string CompanyOnly = "CompanyOnly";
    public const string AdminOnly = "AdminOnly";

    /// <summary>
    /// Signed in as a company AND approved by an admin. An unverified company can
    /// sign in and upload documents; everything else - the talent pool above all -
    /// stays shut.
    /// </summary>
    public const string VerifiedCompanyOnly = "VerifiedCompanyOnly";
}

public sealed class VerifiedCompanyRequirement : IAuthorizationRequirement;

public sealed class VerifiedCompanyHandler(IAuthorizationQueries queries)
    : AuthorizationHandler<VerifiedCompanyRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, VerifiedCompanyRequirement requirement)
    {
        var claim = context.User.FindFirst(JwtTokenService.CompanyIdClaim)?.Value;

        if (!long.TryParse(claim, out var companyId))
        {
            return;
        }

        // Checked against the database on every request rather than trusted from
        // the token: verification can be revoked, and an access token issued
        // before that would otherwise stay valid until it expired.
        if (await queries.IsCompanyVerifiedAsync(companyId, CancellationToken.None))
        {
            context.Succeed(requirement);
        }
    }
}
