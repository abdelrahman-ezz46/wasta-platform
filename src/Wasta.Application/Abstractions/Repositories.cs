using Wasta.Domain.Companies;
using Wasta.Domain.Identity;
using Wasta.Domain.Seekers;

namespace Wasta.Application.Abstractions;

// Repositories rather than an exposed DbContext: the Application layer is not
// allowed to reference EF Core, which is enforced by an architecture test. That
// keeps persistence swappable and stops LINQ-to-entities leaking into use cases.

public interface IUserAccountRepository
{
    Task<UserAccount?> FindByEmailAsync(string email, CancellationToken ct = default);

    Task<UserAccount?> FindByIdAsync(long id, CancellationToken ct = default);

    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);

    void Add(UserAccount user);
}

public interface IJobSeekerRepository
{
    Task<JobSeeker?> FindByUserIdAsync(long userId, CancellationToken ct = default);

    void Add(JobSeeker seeker);

    void AddProfile(JobSeekerProfile profile);
}

public interface ICompanyRepository
{
    Task<Company?> FindByUserIdAsync(long userId, CancellationToken ct = default);

    Task<bool> NormalizedNameExistsAsync(string normalizedName, CancellationToken ct = default);

    void Add(Company company);
}

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default);

    Task RevokeFamilyAsync(long familyId, DateTimeOffset now, CancellationToken ct = default);

    void Add(RefreshToken token);
}

/// <summary>
/// Read-only checks the authorization layer needs. Returns primitives so the
/// web layer can enforce policy without handling domain entities.
/// </summary>
public interface IAuthorizationQueries
{
    Task<bool> IsCompanyVerifiedAsync(long companyId, CancellationToken ct = default);
}

public interface IAccountTokenRepository
{
    Task<Domain.Identity.AccountToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>
    /// Retires this user's outstanding tokens of a purpose. Issuing a new reset
    /// link must kill the previous one, or requesting two leaves a spare valid
    /// credential sitting in an inbox.
    /// </summary>
    Task InvalidateOutstandingAsync(
        long userId, Domain.Identity.AccountTokenPurpose purpose, DateTimeOffset now,
        CancellationToken ct = default);

    void Add(Domain.Identity.AccountToken token);

    /// <summary>Ends every session for a user. Used after a password reset.</summary>
    Task RevokeAllRefreshTokensAsync(long userId, DateTimeOffset now, CancellationToken ct = default);
}
