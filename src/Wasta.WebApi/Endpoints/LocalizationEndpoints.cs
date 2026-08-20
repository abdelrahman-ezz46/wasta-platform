using System.Security.Claims;
using Wasta.Application.Features.Localization;
using Wasta.WebApi.Auth;

namespace Wasta.WebApi.Endpoints;

public static class LocalizationEndpoints
{
    public sealed record SetLanguageRequest(string Language);

    public static IEndpointRouteBuilder MapLocalizationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/reference", async (
            ICurrentLanguage language, GetReferenceDataHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(language.Value, ct)))
        // Anonymous: the sign-up form needs the track list before anyone has an
        // account to sign in with.
        .AllowAnonymous()
        .WithTags("Reference")
        .WithSummary(
            "Every lookup list a client needs to render, translated per Accept-Language or ?lang.")
        .Produces<ReferenceData>();

        app.MapPut("/api/me/language", async (
            SetLanguageRequest body, ClaimsPrincipal user,
            SetLanguageHandler handler, CancellationToken ct) =>
        {
            var userId = user.UserId();
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(new SetLanguageCommand(userId.Value, body.Language), ct);

            return result.IsSuccess
                ? Results.Ok(new { language = result.Value })
                : ProblemMapping.ToProblem(result.Error);
        })
        .RequireAuthorization()
        .WithTags("Reference")
        .WithSummary(
            "Set the account's language. Drives notifications, which are sent long after any "
            + "request header is gone.")
        .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }
}
