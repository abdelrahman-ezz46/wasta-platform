using FluentValidation;
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

    private static IResult ToResponse(Result<AuthResult> result) => ProblemMapping.ToResponse(result);
}
