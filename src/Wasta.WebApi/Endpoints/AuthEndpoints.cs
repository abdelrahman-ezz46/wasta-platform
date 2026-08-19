using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Wasta.Application.Common;
using Wasta.Application.Features.Auth;

namespace Wasta.WebApi.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register/seeker", async (
            RegisterSeekerCommand command,
            IValidator<RegisterSeekerCommand> validator,
            RegisterSeekerHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(command, ct);
            if (!validation.IsValid)
            {
                return ValidationProblem(validation);
            }

            return ToResponse(await handler.HandleAsync(command, ct));
        })
        .WithName("RegisterSeeker")
        .WithSummary("Create a job seeker account and sign in.")
        .Produces<AuthResult>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/register/company", async (
            RegisterCompanyCommand command,
            IValidator<RegisterCompanyCommand> validator,
            RegisterCompanyHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(command, ct);
            if (!validation.IsValid)
            {
                return ValidationProblem(validation);
            }

            return ToResponse(await handler.HandleAsync(command, ct));
        })
        .WithName("RegisterCompany")
        .WithSummary("Register a company. Starts unverified, pending admin approval.")
        .Produces<AuthResult>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/login", async (
            LoginCommand command,
            IValidator<LoginCommand> validator,
            LoginHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(command, ct);
            if (!validation.IsValid)
            {
                return ValidationProblem(validation);
            }

            return ToResponse(await handler.HandleAsync(command, ct));
        })
        .WithName("Login")
        .WithSummary("Exchange credentials for an access and refresh token.")
        .Produces<AuthResult>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh", async (
            RefreshCommand command,
            IValidator<RefreshCommand> validator,
            RefreshHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(command, ct);
            if (!validation.IsValid)
            {
                return ValidationProblem(validation);
            }

            return ToResponse(await handler.HandleAsync(command, ct));
        })
        .WithName("Refresh")
        .WithSummary("Rotate a refresh token. Reusing a spent token ends the whole session.")
        .Produces<AuthResult>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static IResult ValidationProblem(FluentValidation.Results.ValidationResult validation) =>
        Results.ValidationProblem(validation.ToDictionary());

    /// <summary>
    /// Maps an application error code onto a status. Authentication failures are
    /// all 401 with one message, so the response cannot be used to work out
    /// which email addresses exist.
    /// </summary>
    private static IResult ToResponse(Result<AuthResult> result)
    {
        if (result.IsSuccess)
        {
            return Results.Ok(result.Value);
        }

        var (status, title) = result.Error.Code switch
        {
            "auth.email_taken" or "company.name_taken" => (StatusCodes.Status409Conflict, "Conflict"),
            "auth.invalid_credentials" or "auth.invalid_refresh_token" or "auth.refresh_reused"
                => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            _ => (StatusCodes.Status400BadRequest, "Bad request"),
        };

        return Results.Problem(
            title: title,
            detail: result.Error.Message,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code });
    }
}
