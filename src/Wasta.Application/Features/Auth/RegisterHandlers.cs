using Wasta.Application.Abstractions;
using Wasta.Application.Common;
using Wasta.Domain.Companies;
using Wasta.Domain.Identity;
using Wasta.Domain.Seekers;

namespace Wasta.Application.Features.Auth;

public class RegisterSeekerHandler(
    IUserAccountRepository users,
    IJobSeekerRepository seekers,
    IPasswordHasher hasher,
    ITokenService tokens,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<AuthResult>> HandleAsync(RegisterSeekerCommand command, CancellationToken ct = default)
    {
        var email = command.Email.Trim().ToLowerInvariant();

        if (await users.EmailExistsAsync(email, ct))
        {
            return Result.Failure<AuthResult>("auth.email_taken", "An account with that email already exists.");
        }

        var now = clock.UtcNow;
        var user = new UserAccount(email, hasher.Hash(command.Password), UserRole.Seeker);
        users.Add(user);

        // Saved before the seeker row so the identity key exists to hang it off.
        await unitOfWork.SaveChangesAsync(ct);

        var seeker = new JobSeeker(user.Id, command.FullName, command.TrackId, command.PhoneNumber, now);
        seekers.Add(seeker);
        await unitOfWork.SaveChangesAsync(ct);

        // The profile row always exists, empty, so later edits are updates and
        // never have to branch on whether it has been created yet.
        seekers.AddProfile(new JobSeekerProfile(seeker.Id));

        var auth = await AuthTokenIssuer.IssueAsync(
            user, seeker.Id, null, tokens, refreshTokens, unitOfWork, now, ct);

        return Result.Success(auth);
    }
}

public class RegisterCompanyHandler(
    IUserAccountRepository users,
    ICompanyRepository companies,
    IPasswordHasher hasher,
    ITokenService tokens,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<AuthResult>> HandleAsync(RegisterCompanyCommand command, CancellationToken ct = default)
    {
        var email = command.WorkEmail.Trim().ToLowerInvariant();

        if (await users.EmailExistsAsync(email, ct))
        {
            return Result.Failure<AuthResult>("auth.email_taken", "An account with that email already exists.");
        }

        var normalized = Company.Normalize(command.CompanyName);
        if (await companies.NormalizedNameExistsAsync(normalized, ct))
        {
            return Result.Failure<AuthResult>(
                "company.name_taken", "A company with that name is already registered.");
        }

        var now = clock.UtcNow;
        var user = new UserAccount(email, hasher.Hash(command.Password), UserRole.Company);
        users.Add(user);
        await unitOfWork.SaveChangesAsync(ct);

        // Starts unverified. Signing in works; reaching the talent pool does not,
        // until an admin approves the documents.
        var company = new Company(
            user.Id, command.CompanyName, command.Website, command.CompanySize, command.IndustryId, now);
        companies.Add(company);
        await unitOfWork.SaveChangesAsync(ct);

        var auth = await AuthTokenIssuer.IssueAsync(
            user, null, company.Id, tokens, refreshTokens, unitOfWork, now, ct);

        return Result.Success(auth);
    }
}
