using Wasta.Application.Common;

namespace Wasta.WebApi.Endpoints;

/// <summary>
/// One place that decides which HTTP status an application error code becomes.
/// Scattering this across endpoints is how the same condition ends up returning
/// 400 on one route and 409 on another.
/// </summary>
public static class ProblemMapping
{
    public static IResult ToProblem(Error error)
    {
        var (status, title) = error.Code switch
        {
            // Ownership failures report "not found" so the API cannot be used to
            // discover which ids exist.
            "attempt.not_found" or "job.not_found" or "application.not_found"
                or "candidate.not_found" or "company.not_found" or "topup.not_found"
                or "notification.not_found" or "profile.not_found"
                => (StatusCodes.Status404NotFound, "Not found"),

            "auth.email_taken" or "company.name_taken" => (StatusCodes.Status409Conflict, "Conflict"),

            "auth.invalid_credentials" or "auth.invalid_refresh_token" or "auth.refresh_reused"
                => (StatusCodes.Status401Unauthorized, "Unauthorized"),

            // State conflicts: the request is well-formed, the resource is not in
            // a state that allows it.
            "attempt.expired" or "attempt.not_in_progress" or "attempt.not_submitted"
                or "assessment.retake_too_soon"
                or "job.active_limit_reached" or "job.closed" or "job.already_closed"
                or "application.limit_reached" or "application.withdrawn" or "application.already_withdrawn"
                or "credits.insufficient" or "company.already_approved" or "topup.not_pending"
                => (StatusCodes.Status409Conflict, "Conflict"),

            "assessment.no_active_form" or "assessment.no_scoring_rules"
                => (StatusCodes.Status503ServiceUnavailable, "Not available"),

            _ => (StatusCodes.Status400BadRequest, "Bad request"),
        };

        return Results.Problem(
            title: title,
            detail: error.Message,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    public static IResult ToResponse<T>(Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);

    public static IResult ToResponse(Result result) =>
        result.IsSuccess ? Results.NoContent() : ToProblem(result.Error);
}
