using Microsoft.EntityFrameworkCore;
using Wasta.Application.Abstractions;
using Wasta.Application.Common;
using Wasta.Application.Features.Credits;
using Wasta.Domain.Companies;
using Wasta.Domain.Credits;

namespace Wasta.Infrastructure.Persistence.Repositories;

public sealed class CreditQueries(WastaDbContext db) : ICreditQueries
{
    public async Task<int> GetBalanceAsync(long companyId, CancellationToken ct = default) =>
        await db.CreditLedgerEntries
            .Where(e => e.CompanyId == companyId)
            .SumAsync(e => (int?)e.Delta, ct) ?? 0;

    public async Task<PagedResult<LedgerEntryView>> GetLedgerAsync(
        long companyId, PageRequest page, CancellationToken ct = default)
    {
        var query = db.CreditLedgerEntries.AsNoTracking().Where(e => e.CompanyId == companyId);
        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(e => new LedgerEntryView(
                e.Id, e.Delta, e.Reason.ToString(), e.BalanceAfter, e.Note, e.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<LedgerEntryView>(items, page.Page, page.PageSize, total);
    }

    private IQueryable<TopUpRequestView> TopUpViews(IQueryable<CreditTopUpRequest> source) =>
        from r in source
        join c in db.Companies.AsNoTracking() on r.CompanyId equals c.Id
        select new TopUpRequestView(
            r.Id, r.CompanyId, c.Name, r.CreditsRequested, r.Amount, r.Currency,
            r.State.ToString(), r.Note, r.CreatedAt, r.ReviewedAt);

    public async Task<PagedResult<TopUpRequestView>> GetTopUpRequestsForCompanyAsync(
        long companyId, PageRequest page, CancellationToken ct = default)
    {
        var query = db.CreditTopUpRequests.AsNoTracking().Where(r => r.CompanyId == companyId);
        var total = await query.CountAsync(ct);

        var ordered = query.OrderByDescending(r => r.CreatedAt).Skip(page.Skip).Take(page.PageSize);
        var items = await TopUpViews(ordered).ToListAsync(ct);

        return new PagedResult<TopUpRequestView>(items, page.Page, page.PageSize, total);
    }

    public async Task<PagedResult<TopUpRequestView>> GetPendingTopUpRequestsAsync(
        PageRequest page, CancellationToken ct = default)
    {
        var query = db.CreditTopUpRequests.AsNoTracking().Where(r => r.State == TopUpState.Pending);
        var total = await query.CountAsync(ct);

        // Oldest first: a review queue that shows newest first leaves the
        // longest-waiting company waiting longest.
        var ordered = query.OrderBy(r => r.CreatedAt).Skip(page.Skip).Take(page.PageSize);
        var items = await TopUpViews(ordered).ToListAsync(ct);

        return new PagedResult<TopUpRequestView>(items, page.Page, page.PageSize, total);
    }

    public async Task<PagedResult<PendingCompanyView>> GetPendingCompaniesAsync(
        PageRequest page, CancellationToken ct = default)
    {
        var query = db.Companies.AsNoTracking().Where(c => c.VerificationState == VerificationState.Pending);
        var total = await query.CountAsync(ct);

        var ordered = query.OrderBy(c => c.CreatedAt).Skip(page.Skip).Take(page.PageSize);

        var items = await (
            from c in ordered
            join u in db.UserAccounts.AsNoTracking() on c.UserId equals u.Id
            select new PendingCompanyView(
                c.Id,
                c.Name,
                u.Email,
                c.Website,
                db.CompanyDocuments.Count(d => d.CompanyId == c.Id),
                c.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<PendingCompanyView>(items, page.Page, page.PageSize, total);
    }
}

public sealed class CreditRepository(WastaDbContext db) : ICreditRepository
{
    public void AddLedgerEntry(CreditLedgerEntry entry) => db.CreditLedgerEntries.Add(entry);

    public void AddTopUpRequest(CreditTopUpRequest request) => db.CreditTopUpRequests.Add(request);

    public Task<CreditTopUpRequest?> FindTopUpRequestAsync(long requestId, CancellationToken ct = default) =>
        db.CreditTopUpRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);

    public Task<bool> PaymentMethodExistsAsync(int paymentMethodId, CancellationToken ct = default) =>
        db.PaymentMethods.AnyAsync(p => p.Id == paymentMethodId, ct);
}

public sealed class CompanyRepositoryForAdmin(WastaDbContext db) : ICompanyRepositoryForAdmin
{
    public Task<Company?> FindAsync(long companyId, CancellationToken ct = default) =>
        db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct);

    /// <summary>
    /// Whether a trial grant was ever issued. Checked separately from the
    /// verification state so that reject-then-approve cycles cannot mint a
    /// fresh three credits each time round.
    /// </summary>
    public Task<bool> HasTrialGrantAsync(long companyId, CancellationToken ct = default) =>
        db.CreditLedgerEntries.AnyAsync(
            e => e.CompanyId == companyId && e.Reason == CreditReason.TrialGrant, ct);
}
