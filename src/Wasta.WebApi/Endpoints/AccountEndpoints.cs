using System.Security.Claims;
using FluentValidation;
using Wasta.Application.Features.Auth;
using Wasta.WebApi.Auth;

namespace Wasta.WebApi.Endpoints;

public static class AccountEndpoints
{
    public sealed record ConfirmEmailRequest(string Token);

    public sealed record LogoutRequest(string? RefreshToken, bool AllSessions = false);

    public sealed record ForgotPasswordRequest(string Email);

    public sealed record ResendVerificationRequest(string Email);

    public sealed record ResetPasswordRequest(string Token, string NewPassword);

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth")
            .WithTags("Account")
            .RequireRateLimiting(RateLimiting.AuthPolicy);

        auth.MapPost("/verify-email/request", async (
            ClaimsPrincipal user, RequestEmailVerificationHandler handler, CancellationToken ct) =>
        {
            var userId = user.UserId();
            return userId is null
                ? Results.Unauthorized()
                : ProblemMapping.ToResponse(await handler.HandleAsync(userId.Value, ct));
        })
        .RequireAuthorization()
        .WithSummary("Send a fresh confirmation link. Any previous link stops working.");

        auth.MapPost("/verify-email/resend", async (
            ResendVerificationRequest body, ResendEmailVerificationHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new ResendEmailVerificationCommand(body.Email), ct);

            // Always 202 - registered or not, confirmed or not, the answer is
            // byte-identical. Same reasoning as forgot-password.
            return Results.Accepted();
        })
        .AllowAnonymous()
        .WithSummary("Send a fresh confirmation link without signing in. Always accepted.")
        .Produces(StatusCodes.Status202Accepted);

        auth.MapPost("/verify-email/confirm", async (
            ConfirmEmailRequest body, ConfirmEmailHandler handler, CancellationToken ct) =>
            ProblemMapping.ToResponse(await handler.HandleAsync(new ConfirmEmailCommand(body.Token), ct)))
        .AllowAnonymous()
        .WithSummary("Confirm an email address from an emailed link.")
        .ProducesProblem(StatusCodes.Status400BadRequest);

        auth.MapPost("/forgot-password", async (
            ForgotPasswordRequest body, ForgotPasswordHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new ForgotPasswordCommand(body.Email), ct);

            // Always 202, registered or not. Reporting "no such account" would
            // turn this into a membership oracle, for the same reason login
            // gives one message for both failure modes.
            return Results.Accepted();
        })
        .AllowAnonymous()
        .WithSummary("Request a reset link. Always accepted, whether or not the address is registered.")
        .Produces(StatusCodes.Status202Accepted);

        auth.MapPost("/reset-password", async (
            ResetPasswordRequest body,
            IValidator<ResetPasswordCommand> validator,
            ResetPasswordHandler handler,
            CancellationToken ct) =>
        {
            var command = new ResetPasswordCommand(body.Token, body.NewPassword);

            var validation = await validator.ValidateAsync(command, ct);
            if (!validation.IsValid)
            {
                return Results.ValidationProblem(validation.ToDictionary());
            }

            return ProblemMapping.ToResponse(await handler.HandleAsync(command, ct));
        })
        .AllowAnonymous()
        .WithSummary("Set a new password from a reset link. Ends every existing session.")
        .ProducesProblem(StatusCodes.Status400BadRequest);

        auth.MapPost("/logout", async (
            LogoutRequest body, ClaimsPrincipal user, LogoutHandler handler, CancellationToken ct) =>
        {
            var userId = user.UserId();
            return userId is null
                ? Results.Unauthorized()
                : ProblemMapping.ToResponse(
                    await handler.HandleAsync(
                        userId.Value, new LogoutCommand(body.RefreshToken, body.AllSessions), ct));
        })
        .RequireAuthorization()
        .WithSummary("End a session by revoking its refresh token, or every session with allSessions.");

        var me = app.MapGroup("/api/me").WithTags("Account").RequireAuthorization();

        me.MapGet("/export", async (
            ClaimsPrincipal user, ExportPersonalDataHandler handler, CancellationToken ct) =>
        {
            var userId = user.UserId();
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(userId.Value, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : ProblemMapping.ToProblem(result.Error);
        })
        .WithSummary("Everything held about the signed-in account. PDPL right of access.")
        .Produces<PersonalDataExport>();

        me.MapDelete("/", async (
            ClaimsPrincipal user, DeleteAccountHandler handler, CancellationToken ct) =>
        {
            var userId = user.UserId();
            return userId is null
                ? Results.Unauthorized()
                : ProblemMapping.ToResponse(await handler.HandleAsync(userId.Value, ct));
        })
        .WithSummary(
            "Erase the account. Identifying data is scrubbed; credit and unlock records survive "
            + "as the other party's financial history.")
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
