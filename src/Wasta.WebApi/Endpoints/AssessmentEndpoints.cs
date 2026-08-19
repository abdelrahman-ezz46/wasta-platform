using System.Security.Claims;
using Wasta.Application.Features.Assessments;
using Wasta.Infrastructure.Identity;
using Wasta.WebApi.Auth;

namespace Wasta.WebApi.Endpoints;

public static class AssessmentEndpoints
{
    public static IEndpointRouteBuilder MapAssessmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/assessments")
            .WithTags("Assessments")
            .RequireAuthorization(Policies.SeekerOnly);

        group.MapPost("/tracks/{trackId:int}/attempts", async (
            int trackId, ClaimsPrincipal user, StartAttemptHandler handler, CancellationToken ct) =>
        {
            var seekerId = user.SeekerId();
            return seekerId is null
                ? Results.NotFound()
                : ProblemMapping.ToResponse(
                    await handler.HandleAsync(new StartAttemptCommand(seekerId.Value, trackId), ct));
        })
        .WithName("StartAttempt")
        .WithSummary("Open an attempt on a track. Enforces the 30-day per-track retake cooldown.")
        .Produces<StartAttemptResult>()
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/attempts/{attemptId:long}", async (
            long attemptId, ClaimsPrincipal user, GetAttemptHandler handler, CancellationToken ct) =>
        {
            var seekerId = user.SeekerId();
            return seekerId is null
                ? Results.NotFound()
                : ProblemMapping.ToResponse(await handler.HandleAsync(attemptId, seekerId.Value, ct));
        })
        .WithName("GetAttempt")
        .WithSummary("The attempt's questions, saved answers, and remaining time. Never the answer key.")
        .Produces<AttemptView>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/attempts/{attemptId:long}/answers/{questionId:long}", async (
            long attemptId,
            long questionId,
            SaveAnswerRequest body,
            ClaimsPrincipal user,
            SaveAnswerHandler handler,
            CancellationToken ct) =>
        {
            var seekerId = user.SeekerId();
            if (seekerId is null)
            {
                return Results.NotFound();
            }

            var command = new SaveAnswerCommand(
                attemptId, seekerId.Value, questionId, body.SelectedOptionId, body.FlaggedForReview);

            return ProblemMapping.ToResponse(await handler.HandleAsync(command, ct));
        })
        .WithName("SaveAnswer")
        .WithSummary("Save or change one answer. Idempotent, so a retry is safe.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/attempts/{attemptId:long}/submit", async (
            long attemptId, ClaimsPrincipal user, SubmitAttemptHandler handler, CancellationToken ct) =>
        {
            var seekerId = user.SeekerId();
            return seekerId is null
                ? Results.NotFound()
                : ProblemMapping.ToResponse(
                    await handler.HandleAsync(new SubmitAttemptCommand(attemptId, seekerId.Value), ct));
        })
        .WithName("SubmitAttempt")
        .WithSummary("Grade and close the attempt. Rejected server-side if the time limit has passed.")
        .Produces<ResultsView>()
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/attempts/{attemptId:long}/results", async (
            long attemptId, ClaimsPrincipal user, GetResultsHandler handler, CancellationToken ct) =>
        {
            var seekerId = user.SeekerId();
            return seekerId is null
                ? Results.NotFound()
                : ProblemMapping.ToResponse(await handler.HandleAsync(attemptId, seekerId.Value, ct));
        })
        .WithName("GetResults")
        .WithSummary("Score, percentile, section breakdown, band feedback, and skill gaps.")
        .Produces<ResultsView>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public sealed record SaveAnswerRequest(long? SelectedOptionId, bool FlaggedForReview);

    private static long? SeekerId(this ClaimsPrincipal user) =>
        long.TryParse(user.FindFirst(JwtTokenService.SeekerIdClaim)?.Value, out var id) ? id : null;
}
