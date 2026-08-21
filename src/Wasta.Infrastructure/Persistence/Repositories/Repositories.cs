using Microsoft.EntityFrameworkCore;
using Wasta.Application.Abstractions;
using Wasta.Domain.Companies;
using Wasta.Domain.Identity;
using Wasta.Domain.Seekers;

namespace Wasta.Infrastructure.Persistence.Repositories;

public sealed class UserAccountRepository(WastaDbContext db) : IUserAccountRepository
{
    public Task<UserAccount?> FindByEmailAsync(string email, CancellationToken ct = default) =>
        db.UserAccounts.FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<UserAccount?> FindByIdAsync(long id, CancellationToken ct = default) =>
        db.UserAccounts.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default) =>
        db.UserAccounts.AnyAsync(u => u.Email == email, ct);

    public void Add(UserAccount user) => db.UserAccounts.Add(user);
}

public sealed class JobSeekerRepository(WastaDbContext db) : IJobSeekerRepository
{
    public Task<JobSeeker?> FindByUserIdAsync(long userId, CancellationToken ct = default) =>
        db.JobSeekers.FirstOrDefaultAsync(s => s.UserId == userId, ct);

    public void Add(JobSeeker seeker) => db.JobSeekers.Add(seeker);

    public void AddProfile(JobSeekerProfile profile) => db.JobSeekerProfiles.Add(profile);
}

public sealed class CompanyRepository(WastaDbContext db) : ICompanyRepository
{
    public Task<Company?> FindByUserIdAsync(long userId, CancellationToken ct = default) =>
        db.Companies.FirstOrDefaultAsync(c => c.UserId == userId, ct);

    public Task<bool> NormalizedNameExistsAsync(string normalizedName, CancellationToken ct = default) =>
        db.Companies.AnyAsync(c => c.NormalizedName == normalizedName, ct);

    public void Add(Company company) => db.Companies.Add(company);
}

public sealed class RefreshTokenRepository(WastaDbContext db) : IRefreshTokenRepository
{
    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default) =>
        db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task RevokeFamilyAsync(long familyId, DateTimeOffset now, CancellationToken ct = default)
    {
        var family = await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in family)
        {
            token.Revoke(now);
        }
    }

    public async Task RevokeAllForUserAsync(long userId, DateTimeOffset now, CancellationToken ct = default)
    {
        var live = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in live)
        {
            token.Revoke(now);
        }
    }

    public void Add(RefreshToken token) => db.RefreshTokens.Add(token);
}

public sealed class UnitOfWork(WastaDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task<T> InTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        // Already inside one - joining it rather than nesting, since Npgsql has
        // no nested transactions and beginning a second would throw.
        if (db.Database.CurrentTransaction is not null)
        {
            return await operation(ct);
        }

        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var result = await operation(ct);

            await transaction.CommitAsync(ct);
            return result;
        });
    }
}

public sealed class AuthorizationQueries(WastaDbContext db) : IAuthorizationQueries
{
    public Task<bool> IsCompanyVerifiedAsync(long companyId, CancellationToken ct = default) =>
        db.Companies.AnyAsync(
            c => c.Id == companyId && c.VerificationState == Domain.Companies.VerificationState.Approved, ct);
}

public sealed class MeQueries(WastaDbContext db) : Wasta.Application.Features.Me.IMeQueries
{
    public async Task<Wasta.Application.Features.Me.SeekerSummary?> GetSeekerAsync(
        long seekerId, CancellationToken ct = default)
    {
        var row = await (
            from s in db.JobSeekers.AsNoTracking()
            join u in db.UserAccounts.AsNoTracking() on s.UserId equals u.Id
            join p in db.JobSeekerProfiles.AsNoTracking() on s.Id equals p.JobSeekerId into profiles
            from p in profiles.DefaultIfEmpty()
            where s.Id == seekerId
            select new { s.Id, s.FullName, u.Email, s.TrackId, p.ProfileStrength, p.VisibleToCompanies })
            .FirstOrDefaultAsync(ct);

        return row is null
            ? null
            : new Wasta.Application.Features.Me.SeekerSummary(
                row.Id, row.FullName, row.Email, row.TrackId, row.ProfileStrength, row.VisibleToCompanies);
    }

    public async Task<Wasta.Application.Features.Me.CompanySummary?> GetCompanyAsync(
        long companyId, CancellationToken ct = default)
    {
        var row = await (
            from c in db.Companies.AsNoTracking()
            join u in db.UserAccounts.AsNoTracking() on c.UserId equals u.Id
            where c.Id == companyId
            select new { c.Id, c.Name, u.Email, c.VerificationState })
            .FirstOrDefaultAsync(ct);

        return row is null
            ? null
            : new Wasta.Application.Features.Me.CompanySummary(
                row.Id,
                row.Name,
                row.Email,
                row.VerificationState.ToString(),
                row.VerificationState == Domain.Companies.VerificationState.Approved);
    }

    public async Task<Wasta.Application.Features.Me.CreditBalance> GetCreditBalanceAsync(
        long companyId, CancellationToken ct = default)
    {
        // Sum of deltas. BalanceAfter exists for reconciliation, but the sum is
        // the truth, so a corrupt carried value cannot silently grant credits.
        var balance = await db.CreditLedgerEntries
            .Where(e => e.CompanyId == companyId)
            .SumAsync(e => (int?)e.Delta, ct) ?? 0;

        return new Wasta.Application.Features.Me.CreditBalance(companyId, balance);
    }
}

public sealed class LoggerAdapter(Microsoft.Extensions.Logging.ILogger<LoggerAdapter> logger) : ILoggerAdapter
{
    public void Warn(string template, params object?[] args) =>
        Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger, template, args);
}
