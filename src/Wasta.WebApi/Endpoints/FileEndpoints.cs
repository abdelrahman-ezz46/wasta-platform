using System.Security.Claims;
using Wasta.Application.Abstractions;
using Wasta.Application.Features.Files;
using Wasta.Domain.Companies;
using Wasta.WebApi.Auth;

namespace Wasta.WebApi.Endpoints;

public static class FileEndpoints
{
    public static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/seekers/me/cv", async (
            IFormFile file, ClaimsPrincipal user, UploadCvHandler handler,
            IFileUrlSigner signer, IClock clock, CancellationToken ct) =>
        {
            var seekerId = user.SeekerId();
            if (seekerId is null)
            {
                return Results.NotFound();
            }

            await using var content = file.OpenReadStream();
            var payload = new UploadPayload(file.FileName, file.ContentType, file.Length, content);

            var result = await handler.HandleAsync(seekerId.Value, payload, ct);
            return Respond(result, signer, clock);
        })
        .RequireAuthorization(Policies.SeekerOnly)
        .RequireRateLimiting(RateLimiting.UploadPolicy)
        .DisableAntiforgery()
        .WithTags("Files")
        .WithSummary("Upload a CV. PDF only, 5 MB, checked by signature rather than by extension.")
        .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/seekers/me/applications/{applicationId:long}/files", async (
            long applicationId, IFormFile file, ClaimsPrincipal user,
            UploadApplicationFileHandler handler, IFileUrlSigner signer, IClock clock,
            CancellationToken ct) =>
        {
            var seekerId = user.SeekerId();
            if (seekerId is null)
            {
                return Results.NotFound();
            }

            await using var content = file.OpenReadStream();
            var payload = new UploadPayload(file.FileName, file.ContentType, file.Length, content);

            var result = await handler.HandleAsync(applicationId, seekerId.Value, payload, ct);
            return Respond(result, signer, clock);
        })
        .RequireAuthorization(Policies.SeekerOnly)
        .RequireRateLimiting(RateLimiting.UploadPolicy)
        .DisableAntiforgery()
        .WithTags("Files")
        .WithSummary("Attach work to an application. Another seeker's application reports 404.");

        app.MapPost("/api/companies/me/documents", async (
            IFormFile file, string? documentType, ClaimsPrincipal user,
            UploadCompanyDocumentHandler handler, IFileUrlSigner signer, IClock clock,
            CancellationToken ct) =>
        {
            var companyId = user.CompanyId();
            if (companyId is null)
            {
                return Results.NotFound();
            }

            if (!Enum.TryParse<CompanyDocumentType>(documentType, ignoreCase: true, out var kind))
            {
                kind = CompanyDocumentType.Other;
            }

            await using var content = file.OpenReadStream();
            var payload = new UploadPayload(file.FileName, file.ContentType, file.Length, content);

            var result = await handler.HandleAsync(companyId.Value, kind, payload, ct);
            return Respond(result, signer, clock);
        })
        // CompanyOnly, not VerifiedCompanyOnly: submitting these documents is
        // how a company becomes verified, so requiring verification first would
        // be a closed loop.
        .RequireAuthorization(Policies.CompanyOnly)
        .RequireRateLimiting(RateLimiting.UploadPolicy)
        .DisableAntiforgery()
        .WithTags("Files")
        .WithSummary("Upload a verification document. Available before verification, by design.");

        app.MapGet("/api/files/{**key}", async (
            string key, string? token, IFileStore store, IFileUrlSigner signer, CancellationToken ct) =>
        {
            // A valid, unexpired signature is the whole authorisation. Without
            // it the response is 404 rather than 401, so probing for which keys
            // exist tells an attacker nothing.
            if (string.IsNullOrEmpty(token) || !signer.IsValid(key, token))
            {
                return Results.NotFound();
            }

            var metadata = await store.GetMetadataAsync(key, ct);
            var content = await store.OpenReadAsync(key, ct);

            if (metadata is null || content is null)
            {
                return Results.NotFound();
            }

            // Always an attachment, never inline. In production these should be
            // served from a separate origin entirely, so nothing a user uploaded
            // is ever same-origin with the API.
            return Results.File(
                content,
                contentType: metadata.ContentType,
                fileDownloadName: metadata.FileName,
                enableRangeProcessing: false);
        })
        .AllowAnonymous()
        .WithTags("Files")
        .WithSummary("Download a stored file. Requires an unexpired signed token.")
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static IResult Respond(
        Application.Common.Result<StoredFileRef> result, IFileUrlSigner signer, IClock clock)
    {
        if (result.IsFailure)
        {
            return ProblemMapping.ToProblem(result.Error);
        }

        var expiresAt = clock.UtcNow.Add(signer.DefaultLifetime);

        return Results.Ok(new
        {
            key = result.Value.Key,
            fileName = result.Value.FileName,
            contentType = result.Value.ContentType,
            length = result.Value.Length,
            downloadUrl = $"/api/files/{result.Value.Key}?token={signer.CreateToken(result.Value.Key, expiresAt)}",
            expiresAt,
        });
    }
}
