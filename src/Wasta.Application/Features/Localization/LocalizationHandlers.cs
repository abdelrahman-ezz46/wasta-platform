using Wasta.Application.Abstractions;
using Wasta.Application.Common;
using Wasta.Domain.Localization;

namespace Wasta.Application.Features.Localization;

public class GetReferenceDataHandler(IReferenceDataQueries queries)
{
    public Task<ReferenceData> HandleAsync(Language language, CancellationToken ct = default) =>
        queries.GetAsync(language, ct);
}

public class SetLanguageHandler(
    IUserAccountRepository users,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<string>> HandleAsync(SetLanguageCommand command, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(command.UserId, ct);
        if (user is null)
        {
            return Result.Failure<string>("user.not_found", "That account does not exist.");
        }

        // An unrecognised tag is rejected rather than silently falling back to
        // English: silently storing the wrong preference is worse than telling
        // the caller their value was not understood.
        var requested = command.LanguageTag?.Trim().Split('-')[0].ToLowerInvariant();
        if (requested is not (Languages.EnglishCode or Languages.ArabicCode))
        {
            return Result.Failure<string>(
                "language.not_supported",
                $"Supported languages are {Languages.EnglishCode} and {Languages.ArabicCode}.");
        }

        user.SetLanguage(Languages.Parse(requested), clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(user.Language.ToCode());
    }
}
