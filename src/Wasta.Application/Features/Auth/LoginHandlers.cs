using Wasta.Application.Abstractions;
using Wasta.Application.Common;
using Wasta.Domain.Identity;

namespace Wasta.Application.Features.Auth;

public class LoginHandler(
    IUserAccountRepository users,
    IJobSeekerRepository seekers,
    ICompanyRepository companies,
    IPasswordHasher hasher,
    ITokenService tokens,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    /// <summary>
    /// A valid-looking hash for an account that does not exist. Verifying against
    /// it costs the same as a real check, so response time does not reveal which
    /// email addresses are registered.
    /// </summary>
    private const string DummyHash =
        "AQAAAAIAAYagAAAAEHxT1n0zvQ8p2mJ9dK4rWvYcL6sN3fB7gH0iJ2kL4mN6oP8qR0sT2uV4wX6yZ8a==";

    public async Task<Result<AuthResult>> HandleAsync(LoginCommand command, CancellationToken ct = default)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        var user = await users.FindByEmailAsync(email, ct);

        if (user is null)
        {
            // Burn the same work as a real verification before failing.
            hasher.Verify(command.Password, DummyHash);
            return InvalidCredentials();
        }

        if (!hasher.Verify(command.Password, user.PasswordHash))
        {
            return InvalidCredentials();
        }

        // Suspended and deleted accounts fail identically to a wrong password.
        // Saying "your account is suspended" to an unauthenticated caller
        // confirms the address is registered.
        if (!user.CanSignIn)
        {
            return InvalidCredentials();
        }

        var now = clock.UtcNow;
        long? seekerId = null;
        long? companyId = null;

        if (user.Role == UserRole.Seeker)
        {
            seekerId = (await seekers.FindByUserIdAsync(user.Id, ct))?.Id;
        }
        else if (user.Role == UserRole.Company)
        {
            companyId = (await companies.FindByUserIdAsync(user.Id, ct))?.Id;
        }

        var auth = await AuthTokenIssuer.IssueAsync(
            user, seekerId, companyId, tokens, refreshTokens, unitOfWork, now, ct);

        return Result.Success(auth);
    }

    private static Result<AuthResult> InvalidCredentials() =>
        Result.Failure<AuthResult>("auth.invalid_credentials", "Email or password is incorrect.");
}

public class RefreshHandler(
    IRefreshTokenRepository refreshTokens,
    IUserAccountRepository users,
    IJobSeekerRepository seekers,
    ICompanyRepository companies,
    ITokenService tokens,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<AuthResult>> HandleAsync(RefreshCommand command, CancellationToken ct = default)
    {
        var hash = tokens.HashRefreshToken(command.RefreshToken);
        var stored = await refreshTokens.FindByHashAsync(hash, ct);
        var now = clock.UtcNow;

        if (stored is null)
        {
            return Invalid();
        }

        // Presenting an already-rotated token means it leaked: the legitimate
        // client would be holding its successor. Revoke the whole chain rather
        // than this one link, because an attacker holding any link is a breach.
        if (stored.UsedAt is not null)
        {
            await refreshTokens.RevokeFamilyAsync(stored.FamilyId, now, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Failure<AuthResult>(
                "auth.refresh_reused", "This session has been ended for security reasons. Please sign in again.");
        }

        if (!stored.IsActive(now))
        {
            return Invalid();
        }

        var user = await users.FindByIdAsync(stored.UserId, ct);
        if (user is null || !user.CanSignIn)
        {
            return Invalid();
        }

        stored.MarkUsed(now);

        long? seekerId = null;
        long? companyId = null;

        if (user.Role == UserRole.Seeker)
        {
            seekerId = (await seekers.FindByUserIdAsync(user.Id, ct))?.Id;
        }
        else if (user.Role == UserRole.Company)
        {
            companyId = (await companies.FindByUserIdAsync(user.Id, ct))?.Id;
        }

        var auth = await AuthTokenIssuer.IssueAsync(
            user, seekerId, companyId, tokens, refreshTokens, unitOfWork, now, ct, stored.FamilyId);

        return Result.Success(auth);
    }

    private static Result<AuthResult> Invalid() =>
        Result.Failure<AuthResult>("auth.invalid_refresh_token", "That refresh token is not valid.");
}
