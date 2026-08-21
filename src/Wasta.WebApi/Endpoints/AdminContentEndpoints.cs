using System.Security.Claims;
using Wasta.Application.Abstractions;
using Wasta.Application.Common;
using Wasta.Application.Features.Admin;
using Wasta.WebApi.Auth;

namespace Wasta.WebApi.Endpoints;

/// <summary>
/// The surface a subject-matter expert and a psychometrician use to load real
/// assessment content. Everything here is admin-only and audited.
/// </summary>
public static class AdminContentEndpoints
{
    public static IEndpointRouteBuilder MapAdminContentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/content")
            .WithTags("Admin content")
            .RequireAuthorization(Policies.AdminOnly);

        group.MapGet("/readiness", async (TrackReadinessHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .WithSummary(
            "Per-track content readiness, counting seeded placeholders separately from real questions.")
        .Produces<IReadOnlyList<TrackReadiness>>();

        // ---- tracks and sections ----

        group.MapPost("/tracks", async (
            CreateTrackCommand command, ClaimsPrincipal user,
            CreateTrackHandler handler, CancellationToken ct) =>
        {
            var adminId = user.UserId();
            if (adminId is null)
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(command, adminId.Value, ct);
            return result.IsSuccess
                ? Results.Created($"/api/admin/content/tracks/{result.Value}", new { trackId = result.Value })
                : ProblemMapping.ToProblem(result.Error);
        })
        .WithSummary("Create a track. Starts inactive — an empty track must not reach the sign-up form.");

        group.MapPut("/tracks/{trackId:int}", async (
            int trackId, UpdateTrackCommand body, ClaimsPrincipal user,
            UpdateTrackHandler handler, CancellationToken ct) =>
        {
            var adminId = user.UserId();
            return adminId is null
                ? Results.Unauthorized()
                : ProblemMapping.ToResponse(
                    await handler.HandleAsync(body with { TrackId = trackId }, adminId.Value, ct));
        })
        .WithSummary("Rename a track, reorder it, or take it live.");

        group.MapPost("/sections", async (
            CreateSectionCommand command, CreateSectionHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/admin/content/sections/{result.Value}", new { sectionId = result.Value })
                : ProblemMapping.ToProblem(result.Error);
        })
        .WithSummary("Add a scored section to a track.");

        // ---- questions ----

        group.MapGet("/tracks/{trackId:int}/questions", async (
            int trackId, int? page, int? pageSize, IAdminContentQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.ListQuestionsAsync(trackId, new PageRequest(page, pageSize), ct)))
        .WithSummary("Every question on a track, with its options and whether it is locked.")
        .Produces<PagedResult<AdminQuestionView>>();

        group.MapPost("/questions", async (
            CreateQuestionCommand command, ClaimsPrincipal user,
            CreateQuestionHandler handler, CancellationToken ct) =>
        {
            var adminId = user.UserId();
            if (adminId is null)
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(command, adminId.Value, ct);
            return result.IsSuccess
                ? Results.Created($"/api/admin/content/questions/{result.Value}", new { questionId = result.Value })
                : ProblemMapping.ToProblem(result.Error);
        })
        .WithSummary("Add a question. Exactly one option must be marked correct.")
        .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPut("/questions/{questionId:long}", async (
            long questionId, UpdateQuestionCommand body, ClaimsPrincipal user,
            UpdateQuestionHandler handler, CancellationToken ct) =>
        {
            var adminId = user.UserId();
            return adminId is null
                ? Results.Unauthorized()
                : ProblemMapping.ToResponse(
                    await handler.HandleAsync(body with { QuestionId = questionId }, adminId.Value, ct));
        })
        .WithSummary("Edit a question. Refused once a submitted attempt has been scored against it.")
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/questions/{questionId:long}/retire", async (
            long questionId, ClaimsPrincipal user,
            DeactivateQuestionHandler handler, CancellationToken ct) =>
        {
            var adminId = user.UserId();
            return adminId is null
                ? Results.Unauthorized()
                : ProblemMapping.ToResponse(await handler.HandleAsync(questionId, adminId.Value, ct));
        })
        .WithSummary("Take a question out of future forms. Allowed even when locked.");

        // ---- forms ----

        group.MapGet("/tracks/{trackId:int}/forms", async (
            int trackId, IAdminContentQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.ListFormsAsync(trackId, ct)))
        .WithSummary("Assessment forms for a track.")
        .Produces<IReadOnlyList<AdminFormView>>();

        group.MapPost("/forms", async (
            CreateFormCommand command, CreateFormHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/admin/content/forms/{result.Value}", new { formId = result.Value })
                : ProblemMapping.ToProblem(result.Error);
        })
        .WithSummary("Create a form. Starts inactive and empty.");

        group.MapPut("/forms/{formId:int}/questions", async (
            int formId, SetFormQuestionsCommand body,
            SetFormQuestionsHandler handler, CancellationToken ct) =>
            ProblemMapping.ToResponse(await handler.HandleAsync(body with { FormId = formId }, ct)))
        .WithSummary("Set a form's questions. Refused once anyone has sat it.")
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/forms/{formId:int}/activate", async (
            int formId, ClaimsPrincipal user, ActivateFormHandler handler, CancellationToken ct) =>
        {
            var adminId = user.UserId();
            return adminId is null
                ? Results.Unauthorized()
                : ProblemMapping.ToResponse(await handler.HandleAsync(formId, adminId.Value, ct));
        })
        .WithSummary("Publish a form. Re-validates composition and retires the track's previous form.");

        // ---- scoring ----

        group.MapGet("/tracks/{trackId:int}/scoring-rules", async (
            int trackId, IAdminContentQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.ListScoringRulesAsync(trackId, ct)))
        .WithSummary("Scoring rule versions for a track, with their bands and weights.")
        .Produces<IReadOnlyList<AdminScoringRuleView>>();

        group.MapPost("/scoring-rules", async (
            CreateScoringRuleCommand command, CreateScoringRuleHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(command, ct);
            return result.IsSuccess
                ? Results.Created(
                    $"/api/admin/content/scoring-rules/{result.Value}", new { ruleVersionId = result.Value })
                : ProblemMapping.ToProblem(result.Error);
        })
        .WithSummary("Create a scoring rule version.");

        group.MapPut("/scoring-rules/{ruleVersionId:int}/bands", async (
            int ruleVersionId, SetBandsCommand body, SetBandsHandler handler, CancellationToken ct) =>
            ProblemMapping.ToResponse(
                await handler.HandleAsync(body with { RuleVersionId = ruleVersionId }, ct)))
        .WithSummary("Set bands. They must tile 0–100 with no gap and no overlap.")
        .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPut("/scoring-rules/{ruleVersionId:int}/weights", async (
            int ruleVersionId, SetWeightsCommand body, SetWeightsHandler handler, CancellationToken ct) =>
            ProblemMapping.ToResponse(
                await handler.HandleAsync(body with { RuleVersionId = ruleVersionId }, ct)))
        .WithSummary("Set section weights. They must sum to 1 and cover every section on the track.")
        .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/scoring-rules/{ruleVersionId:int}/activate", async (
            int ruleVersionId, ClaimsPrincipal user,
            ActivateScoringRuleHandler handler, CancellationToken ct) =>
        {
            var adminId = user.UserId();
            return adminId is null
                ? Results.Unauthorized()
                : ProblemMapping.ToResponse(await handler.HandleAsync(ruleVersionId, adminId.Value, ct));
        })
        .WithSummary("Publish a scoring rule. Re-validates bands and weights first.");

        group.MapPut("/section-feedback", async (
            SetSectionFeedbackCommand command,
            SetSectionFeedbackHandler handler, CancellationToken ct) =>
            ProblemMapping.ToResponse(await handler.HandleAsync(command, ct)))
        .WithSummary(
            "Set the fixed feedback for a section and band. Not locked — it is prose, not an input "
            + "to the score.");

        // ---- translations ----

        group.MapPut("/translations", async (
            SetTranslationCommand command, SetTranslationHandler handler, CancellationToken ct) =>
            ProblemMapping.ToResponse(await handler.HandleAsync(command, ct)))
        .WithSummary("Set a translated name for a reference row, invalidating the cache.");

        return app;
    }
}
