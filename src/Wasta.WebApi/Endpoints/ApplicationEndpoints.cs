using System.Security.Claims;
using Wasta.Application.Common;
using Wasta.Application.Features.Applications;
using Wasta.WebApi.Auth;

namespace Wasta.WebApi.Endpoints;

public static class ApplicationEndpoints
{
    public sealed record UpdateProjectRequest(
        string? ProjectTitle,
        string? Description,
        string? RepoUrl,
        string? LiveDemoUrl);

    public static IEndpointRouteBuilder MapApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/seekers/me/applications")
            .WithTags("Applications")
            .RequireAuthorization(Policies.SeekerOnly);

        group.MapGet("/", async (
            int? page, int? pageSize, ClaimsPrincipal user,
            ListMyApplicationsHandler handler, CancellationToken ct) =>
        {
            var seekerId = user.SeekerId();
            return seekerId is null
                ? Results.NotFound()
                : Results.Ok(await handler.HandleAsync(seekerId.Value, new PageRequest(page, pageSize), ct));
        })
        .WithSummary("The seeker's own applications and their attached projects.")
        .Produces<PagedResult<ApplicationView>>();

        group.MapGet("/{applicationId:long}", async (
            long applicationId, ClaimsPrincipal user, GetMyApplicationHandler handler, CancellationToken ct) =>
        {
            var seekerId = user.SeekerId();
            return seekerId is null
                ? Results.NotFound()
                : ProblemMapping.ToResponse(await handler.HandleAsync(applicationId, seekerId.Value, ct));
        })
        .WithSummary("One application. Another seeker's reports 404, not 403.")
        .Produces<ApplicationView>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{applicationId:long}", async (
            long applicationId, UpdateProjectRequest body, ClaimsPrincipal user,
            UpdateProjectHandler handler, CancellationToken ct) =>
        {
            var seekerId = user.SeekerId();
            if (seekerId is null)
            {
                return Results.NotFound();
            }

            var command = new UpdateProjectCommand(
                applicationId, seekerId.Value, body.ProjectTitle, body.Description, body.RepoUrl, body.LiveDemoUrl);

            return ProblemMapping.ToResponse(await handler.HandleAsync(command, ct));
        })
        .WithSummary("Save the project: description, repo URL, live URL.");

        group.MapPost("/{applicationId:long}/submit", async (
            long applicationId, ClaimsPrincipal user, SubmitProjectHandler handler, CancellationToken ct) =>
        {
            var seekerId = user.SeekerId();
            return seekerId is null
                ? Results.NotFound()
                : ProblemMapping.ToResponse(await handler.HandleAsync(applicationId, seekerId.Value, ct));
        })
        .WithSummary("Mark the project submitted for review.");

        group.MapPost("/{applicationId:long}/withdraw", async (
            long applicationId, ClaimsPrincipal user, WithdrawApplicationHandler handler, CancellationToken ct) =>
        {
            var seekerId = user.SeekerId();
            return seekerId is null
                ? Results.NotFound()
                : ProblemMapping.ToResponse(await handler.HandleAsync(applicationId, seekerId.Value, ct));
        })
        .WithSummary("Withdraw. Frees a slot against the 6-application cap; the record is kept.");

        return app;
    }
}
