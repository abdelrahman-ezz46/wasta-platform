using System.Security.Claims;
using Wasta.Application.Common;
using Wasta.Application.Features.Applications;
using Wasta.Application.Features.Jobs;
using Wasta.WebApi.Auth;

namespace Wasta.WebApi.Endpoints;

public static class JobEndpoints
{
    public sealed record PostJobRequest(
        string Title,
        int TrackId,
        string JobDescription,
        int? WorkTypeId,
        int? LocationId,
        int? EmploymentTypeId,
        SalaryRange? Salary,
        string? ProjectBrief,
        DateOnly? ProjectDeadline,
        IReadOnlyList<int>? SkillIds);

    public sealed record UpdateJobRequest(
        string Title,
        string JobDescription,
        SalaryRange? Salary,
        string? ProjectBrief,
        DateOnly? ProjectDeadline,
        IReadOnlyList<int>? SkillIds);

    public sealed record SetStatusRequest(int StatusId, string? Feedback);

    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        MapCompanyJobs(app);
        MapSeekerBrowsing(app);
        return app;
    }

    private static void MapCompanyJobs(IEndpointRouteBuilder app)
    {
        // Verified only. An unapproved company must not be able to advertise,
        // because a posting is what pulls candidates into contact with them.
        var group = app.MapGroup("/api/companies/me")
            .WithTags("Company jobs")
            .RequireAuthorization(Policies.VerifiedCompanyOnly);

        group.MapPost("/jobs", async (
            PostJobRequest body, ClaimsPrincipal user, PostJobHandler handler, CancellationToken ct) =>
        {
            var companyId = user.CompanyId();
            if (companyId is null)
            {
                return Results.NotFound();
            }

            var command = new PostJobCommand(
                companyId.Value, body.Title, body.TrackId, body.JobDescription,
                body.WorkTypeId, body.LocationId, body.EmploymentTypeId,
                body.Salary, body.ProjectBrief, body.ProjectDeadline, body.SkillIds);

            var result = await handler.HandleAsync(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/jobs/{result.Value}", new { jobPostId = result.Value })
                : ProblemMapping.ToProblem(result.Error);
        })
        .WithSummary("Post a job. Capped at 6 active posts per company.")
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/jobs", async (
            int? page, int? pageSize, ClaimsPrincipal user, ListCompanyJobsHandler handler, CancellationToken ct) =>
        {
            var companyId = user.CompanyId();
            return companyId is null
                ? Results.NotFound()
                : Results.Ok(await handler.HandleAsync(companyId.Value, new PageRequest(page, pageSize), ct));
        })
        .WithSummary("The company's own postings, active first.")
        .Produces<PagedResult<JobSummary>>();

        group.MapPut("/jobs/{jobPostId:long}", async (
            long jobPostId, UpdateJobRequest body, ClaimsPrincipal user,
            UpdateJobHandler handler, CancellationToken ct) =>
        {
            var companyId = user.CompanyId();
            if (companyId is null)
            {
                return Results.NotFound();
            }

            var command = new UpdateJobCommand(
                jobPostId, companyId.Value, body.Title, body.JobDescription,
                body.Salary, body.ProjectBrief, body.ProjectDeadline, body.SkillIds);

            return ProblemMapping.ToResponse(await handler.HandleAsync(command, ct));
        })
        .WithSummary("Edit a posting. Another company's post reports 404.")
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/jobs/{jobPostId:long}/close", async (
            long jobPostId, ClaimsPrincipal user, CloseJobHandler handler, CancellationToken ct) =>
        {
            var companyId = user.CompanyId();
            return companyId is null
                ? Results.NotFound()
                : ProblemMapping.ToResponse(await handler.HandleAsync(jobPostId, companyId.Value, ct));
        })
        .WithSummary("Close a posting. Frees a slot; applications are kept.");

        group.MapGet("/jobs/{jobPostId:long}/applicants", async (
            long jobPostId, int? page, int? pageSize, ClaimsPrincipal user,
            GetJobApplicantsHandler handler, CancellationToken ct) =>
        {
            var companyId = user.CompanyId();
            return companyId is null
                ? Results.NotFound()
                : ProblemMapping.ToResponse(
                    await handler.HandleAsync(jobPostId, companyId.Value, new PageRequest(page, pageSize), ct));
        })
        .WithSummary("Applicants for one of the company's postings. Names appear only once unlocked.")
        .Produces<PagedResult<ApplicantView>>();

        group.MapPut("/applications/{applicationId:long}/status", async (
            long applicationId, SetStatusRequest body, ClaimsPrincipal user,
            SetApplicationStatusHandler handler, CancellationToken ct) =>
        {
            var companyId = user.CompanyId();
            if (companyId is null)
            {
                return Results.NotFound();
            }

            var command = new SetApplicationStatusCommand(
                applicationId, companyId.Value, body.StatusId, body.Feedback);

            return ProblemMapping.ToResponse(await handler.HandleAsync(command, ct));
        })
        .WithSummary("Move an applicant through review. Withdrawal is the applicant's to make, not the company's.");
    }

    private static void MapSeekerBrowsing(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/jobs")
            .WithTags("Jobs")
            .RequireAuthorization(Policies.SeekerOnly);

        group.MapGet("/", async (
            int? trackId, string? search, bool? recommendedOnly, int? page, int? pageSize,
            ClaimsPrincipal user, BrowseJobsHandler handler, CancellationToken ct) =>
        {
            var query = new BrowseJobsQuery(
                user.SeekerId(), trackId, search, recommendedOnly ?? false, page, pageSize);

            return Results.Ok(await handler.HandleAsync(query, ct));
        })
        .WithSummary("Browse open postings. Posts on the seeker's own track are flagged and sorted first.")
        .Produces<PagedResult<JobSummary>>();

        group.MapGet("/{jobPostId:long}", async (
            long jobPostId, ClaimsPrincipal user, GetJobDetailHandler handler, CancellationToken ct) =>
            ProblemMapping.ToResponse(await handler.HandleAsync(jobPostId, user.SeekerId(), ct)))
        .WithSummary("One posting in full, including the project brief and deadline.")
        .Produces<JobDetail>();

        group.MapPost("/{jobPostId:long}/apply", async (
            long jobPostId, ClaimsPrincipal user, ApplyToJobHandler handler, CancellationToken ct) =>
        {
            var seekerId = user.SeekerId();
            if (seekerId is null)
            {
                return Results.NotFound();
            }

            var result = await handler.HandleAsync(new ApplyToJobCommand(seekerId.Value, jobPostId), ct);
            return result.IsSuccess
                ? Results.Created(
                    $"/api/seekers/me/applications/{result.Value}", new { applicationId = result.Value })
                : ProblemMapping.ToProblem(result.Error);
        })
        .WithSummary("Apply, creating the application and its project. Capped at 6 live applications.")
        .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
