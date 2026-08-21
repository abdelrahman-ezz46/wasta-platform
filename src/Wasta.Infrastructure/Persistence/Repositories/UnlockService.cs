using Microsoft.EntityFrameworkCore;
using Wasta.Application.Features.TalentPool;
using System.Text.Json;
using Wasta.Application.Features.Notifications;
using Wasta.Domain.Audit;
using Wasta.Domain.Credits;

namespace Wasta.Infrastructure.Persistence.Repositories;

/// <summary>
/// The one place a credit is spent.
///
/// Three independent guards, because this is where money leaves an account:
///   1. The company row is locked FOR UPDATE, so two requests for the same
///      company serialise instead of both reading the same balance.
///   2. The balance is summed from the ledger inside that lock, never read
///      from a carried counter.
///   3. A unique index on (company_id, job_seeker_id) is the final backstop -
///      if anything ever got past the first two, the insert fails rather than
///      charging twice.
/// </summary>
public sealed class UnlockService(WastaDbContext db, Wasta.Application.Abstractions.IClock clock) : IUnlockService
{
    private const int UnlockCost = 1;

    public async Task<UnlockResult> UnlockAsync(
        long companyId, long jobSeekerId, long actorUserId, CancellationToken ct = default)
    {
        // Opted-out seekers are not unlockable. Checked before any transaction
        // so a hidden candidate never causes a lock to be taken.
        var visible = await (
            from s in db.JobSeekers.AsNoTracking()
            join p in db.JobSeekerProfiles.AsNoTracking() on s.Id equals p.JobSeekerId
            where s.Id == jobSeekerId && p.VisibleToCompanies
            select s.Id).AnyAsync(ct);

        if (!visible)
        {
            return new UnlockResult(UnlockOutcome.CandidateNotFound, null, 0);
        }

        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            // Row lock. Everything below runs with this company's unlocks
            // serialised; another request for the same company waits here.
            _ = await db.Companies
                .FromSql($"SELECT * FROM company WHERE id = {companyId} FOR UPDATE")
                .ToListAsync(ct);

            var existing = await db.ProfileUnlocks
                .FirstOrDefaultAsync(u => u.CompanyId == companyId && u.JobSeekerId == jobSeekerId, ct);

            if (existing is not null)
            {
                var held = await BalanceAsync(companyId, ct);
                await transaction.CommitAsync(ct);
                return new UnlockResult(UnlockOutcome.AlreadyUnlocked, existing.Id, held);
            }

            var balance = await BalanceAsync(companyId, ct);
            if (balance < UnlockCost)
            {
                await transaction.RollbackAsync(ct);
                return new UnlockResult(UnlockOutcome.InsufficientCredits, null, balance);
            }

            var now = clock.UtcNow;

            var entry = CreditLedgerEntry.Debit(
                companyId, UnlockCost, CreditReason.Unlock, balance, actorUserId,
                $"Unlocked candidate {jobSeekerId}.", now);

            db.CreditLedgerEntries.Add(entry);
            await db.SaveChangesAsync(ct);

            var unlock = new ProfileUnlock(companyId, jobSeekerId, entry.Id, now);
            db.ProfileUnlocks.Add(unlock);

            // The seeker is told who unlocked them, matching the "Companies that
            // unlocked your profile" list. Queued inside this transaction, so it
            // cannot survive a rolled-back charge.
            var seekerUserId = await db.JobSeekers
                .Where(js => js.Id == jobSeekerId).Select(js => (long?)js.UserId).FirstOrDefaultAsync(ct);
            var companyName = await db.Companies
                .Where(c => c.Id == companyId).Select(c => c.Name).FirstOrDefaultAsync(ct);

            if (seekerUserId is not null)
            {
                db.Notifications.Add(new Notification(
                    seekerUserId.Value,
                    NotificationKinds.ProfileUnlocked,
                    JsonSerializer.Serialize(new { companyId, companyName }),
                    now));
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // The unique index fired: a concurrent request got there first.
                // Roll back rather than charging a second credit for a candidate
                // this company already holds.
                await transaction.RollbackAsync(ct);

                var already = await db.ProfileUnlocks.AsNoTracking().FirstOrDefaultAsync(
                    u => u.CompanyId == companyId && u.JobSeekerId == jobSeekerId, ct);

                return new UnlockResult(
                    UnlockOutcome.AlreadyUnlocked, already?.Id, await BalanceAsync(companyId, ct));
            }

            // Audited inside the transaction: the record of a spend must not be
            // separable from the spend.
            db.AuditLog.Add(new AuditLogEntry(
                actorUserId,
                "candidate.unlocked",
                "profile_unlock",
                unlock.Id.ToString(),
                JsonSerializer.Serialize(new { companyId, jobSeekerId, balanceAfter = entry.BalanceAfter }),
                now));

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return new UnlockResult(UnlockOutcome.Unlocked, unlock.Id, entry.BalanceAfter);
        });
    }

    private async Task<int> BalanceAsync(long companyId, CancellationToken ct) =>
        await db.CreditLedgerEntries
            .Where(e => e.CompanyId == companyId)
            .SumAsync(e => (int?)e.Delta, ct) ?? 0;
}
