using Wasta.Application.Abstractions;
using Wasta.Application.Common;
using Wasta.Domain.Companies;
using Wasta.Application.Features.Notifications;
using Wasta.Domain.Credits;

namespace Wasta.Application.Features.Credits;

public class GetLedgerHandler(ICreditQueries queries)
{
    public Task<PagedResult<LedgerEntryView>> HandleAsync(
        long companyId, PageRequest page, CancellationToken ct = default) =>
        queries.GetLedgerAsync(companyId, page, ct);
}

public class ListMyTopUpRequestsHandler(ICreditQueries queries)
{
    public Task<PagedResult<TopUpRequestView>> HandleAsync(
        long companyId, PageRequest page, CancellationToken ct = default) =>
        queries.GetTopUpRequestsForCompanyAsync(companyId, page, ct);
}

public class RequestTopUpHandler(
    ICreditRepository credits,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<long>> HandleAsync(RequestTopUpCommand command, CancellationToken ct = default)
    {
        if (command.CreditsRequested <= 0)
        {
            return Result.Failure<long>("topup.credits_invalid", "Requested credits must be greater than zero.");
        }

        if (!await credits.PaymentMethodExistsAsync(command.PaymentMethodId, ct))
        {
            return Result.Failure<long>("topup.payment_method_invalid", "That payment method does not exist.");
        }

        // No money moves here. The company states what it wants, transfers the
        // funds out of band, and an admin issues the credits once the transfer
        // has actually landed.
        var request = new CreditTopUpRequest(
            command.CompanyId,
            command.CreditsRequested,
            command.PaymentMethodId,
            command.Amount,
            command.Currency,
            clock.UtcNow);

        credits.AddTopUpRequest(request);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(request.Id);
    }
}

public class ListPendingCompaniesHandler(ICreditQueries queries)
{
    public Task<PagedResult<PendingCompanyView>> HandleAsync(PageRequest page, CancellationToken ct = default) =>
        queries.GetPendingCompaniesAsync(page, ct);
}

public class ListPendingTopUpsHandler(ICreditQueries queries)
{
    public Task<PagedResult<TopUpRequestView>> HandleAsync(PageRequest page, CancellationToken ct = default) =>
        queries.GetPendingTopUpRequestsAsync(page, ct);
}

public class ApproveCompanyHandler(
    ICompanyRepositoryForAdmin companies,
    ICreditRepository credits,
    ICreditQueries queries,
    IUnitOfWork unitOfWork,
    IClock clock,
    INotificationService notifications,
    INotificationRecipients recipients)
{
    public async Task<Result> HandleAsync(ApproveCompanyCommand command, CancellationToken ct = default)
    {
        return await unitOfWork.InTransactionAsync(async token =>
        {
            var company = await companies.FindAsync(command.CompanyId, token);
            if (company is null)
            {
                return Result.Failure("company.not_found", "That company does not exist.");
            }

            if (company.IsVerified)
            {
                return Result.Failure("company.already_approved", "This company is already approved.");
            }

            var now = clock.UtcNow;
            company.Approve(command.AdminUserId, now);

            // Guarded separately from the approval state so that a company
            // rejected, then approved, then rejected, then approved again ends
            // up with one trial grant rather than four.
            if (!await companies.HasTrialGrantAsync(command.CompanyId, token))
            {
                var balance = await queries.GetBalanceAsync(command.CompanyId, token);

                credits.AddLedgerEntry(CreditLedgerEntry.Credit(
                    command.CompanyId,
                    Company.TrialCredits,
                    CreditReason.TrialGrant,
                    balance,
                    command.AdminUserId,
                    "Trial credits granted on verification.",
                    now));
            }

            var recipientId = await recipients.UserIdForCompanyAsync(command.CompanyId, token);
            if (recipientId is not null)
            {
                notifications.Queue(
                    recipientId.Value,
                    NotificationKinds.CompanyApproved,
                    new { companyId = command.CompanyId, companyName = company.Name });
            }

            await unitOfWork.SaveChangesAsync(token);
            return Result.Success();
        }, ct);
    }
}

public class RejectCompanyHandler(
    ICompanyRepositoryForAdmin companies,
    IUnitOfWork unitOfWork,
    IClock clock,
    INotificationService notifications,
    INotificationRecipients recipients)
{
    public async Task<Result> HandleAsync(RejectCompanyCommand command, CancellationToken ct = default)
    {
        var company = await companies.FindAsync(command.CompanyId, ct);
        if (company is null)
        {
            return Result.Failure("company.not_found", "That company does not exist.");
        }

        company.Reject(command.AdminUserId, command.Note, clock.UtcNow);

        var rejectedRecipient = await recipients.UserIdForCompanyAsync(command.CompanyId, ct);
        if (rejectedRecipient is not null)
        {
            notifications.Queue(
                rejectedRecipient.Value,
                NotificationKinds.CompanyRejected,
                new { companyId = command.CompanyId, companyName = company.Name, note = command.Note });
        }

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}

public class ReviewTopUpHandler(
    ICreditRepository credits,
    ICreditQueries queries,
    IUnitOfWork unitOfWork,
    IClock clock,
    INotificationService notifications,
    INotificationRecipients recipients)
{
    public async Task<Result> HandleAsync(ReviewTopUpCommand command, CancellationToken ct = default)
    {
        return await unitOfWork.InTransactionAsync(async token =>
        {
            var request = await credits.FindTopUpRequestAsync(command.RequestId, token);
            if (request is null)
            {
                return Result.Failure("topup.not_found", "That top-up request does not exist.");
            }

            if (request.State != TopUpState.Pending)
            {
                return Result.Failure("topup.not_pending", "This request has already been reviewed.");
            }

            var now = clock.UtcNow;

            if (!command.Approve)
            {
                request.Reject(command.AdminUserId, command.Note, now);
                await unitOfWork.SaveChangesAsync(token);
                return Result.Success();
            }

            // Approving means the admin has confirmed the transfer arrived. The
            // ledger entry and the state change land together, so a request can
            // never read as approved without the credits existing.
            var balance = await queries.GetBalanceAsync(request.CompanyId, token);

            var entry = CreditLedgerEntry.Credit(
                request.CompanyId,
                request.CreditsRequested,
                CreditReason.TopUp,
                balance,
                command.AdminUserId,
                command.Note ?? "Bank transfer confirmed.",
                now);

            credits.AddLedgerEntry(entry);
            await unitOfWork.SaveChangesAsync(token);

            request.Approve(command.AdminUserId, entry.Id, now);

            var recipientId = await recipients.UserIdForCompanyAsync(request.CompanyId, token);
            if (recipientId is not null)
            {
                notifications.Queue(
                    recipientId.Value,
                    NotificationKinds.CreditsIssued,
                    new
                    {
                        requestId = request.Id,
                        credits = request.CreditsRequested,
                        balance = entry.BalanceAfter,
                    });
            }

            await unitOfWork.SaveChangesAsync(token);

            return Result.Success();
        }, ct);
    }
}
