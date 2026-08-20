using Wasta.Application.Abstractions;
using Wasta.Application.Common;
using Wasta.Domain.Applications;
using Wasta.Domain.Companies;

namespace Wasta.Application.Features.Files;

/// <summary>A seekable stream plus what the client claimed about it.</summary>
public sealed record UploadPayload(string FileName, string? ContentType, long Length, Stream Content);

/// <summary>
/// Validate, scan, store, record - in that order. Storing before scanning would
/// put an unscanned file somewhere it can be fetched from, however briefly.
/// </summary>
internal static class UploadPipeline
{
    private const int SignatureBytes = 16;

    public static async Task<Result<StoredFileRef>> RunAsync(
        FileKind kind,
        UploadPayload payload,
        IFileStore store,
        IVirusScanner scanner,
        CancellationToken ct)
    {
        var buffer = new byte[SignatureBytes];
        var read = await payload.Content.ReadAsync(buffer.AsMemory(0, SignatureBytes), ct);
        payload.Content.Position = 0;

        var validation = FileValidation.Validate(
            kind, payload.FileName, payload.ContentType, payload.Length, buffer.AsSpan(0, read));

        if (!validation.IsValid)
        {
            return Result.Failure<StoredFileRef>(validation.Code!, validation.Message!);
        }

        var scan = await scanner.ScanAsync(payload.Content, ct);
        payload.Content.Position = 0;

        if (!scan.IsClean)
        {
            return Result.Failure<StoredFileRef>(
                "file.infected", "That file was rejected by the malware scanner.");
        }

        var stored = await store.SaveAsync(
            kind, payload.FileName, payload.ContentType!, payload.Content, ct);

        return Result.Success(stored);
    }
}

public class UploadCvHandler(
    IUploadRepository uploads,
    IFileStore store,
    IVirusScanner scanner,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<StoredFileRef>> HandleAsync(
        long seekerId, UploadPayload payload, CancellationToken ct = default)
    {
        var profile = await uploads.FindProfileAsync(seekerId, ct);
        if (profile is null)
        {
            return Result.Failure<StoredFileRef>("profile.not_found", "That profile does not exist.");
        }

        var result = await UploadPipeline.RunAsync(FileKind.Cv, payload, store, scanner, ct);
        if (result.IsFailure)
        {
            return result;
        }

        // Replacing a CV removes the old one rather than orphaning it. A CV is
        // personal data, so keeping superseded copies means keeping data nobody
        // asked us to hold.
        var previous = profile.CvUrl;

        profile.SetCv(result.Value.Key, clock.UtcNow);
        profile.RecomputeStrength(
            await uploads.CountSkillsAsync(seekerId, ct),
            await uploads.SeekerHasTrackAsync(seekerId, ct));

        await unitOfWork.SaveChangesAsync(ct);

        if (!string.IsNullOrEmpty(previous))
        {
            await store.DeleteAsync(previous, ct);
        }

        return result;
    }
}

public class UploadApplicationFileHandler(
    IUploadRepository uploads,
    IFileStore store,
    IVirusScanner scanner,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public const int MaxFilesPerApplication = 10;

    public async Task<Result<StoredFileRef>> HandleAsync(
        long applicationId, long seekerId, UploadPayload payload, CancellationToken ct = default)
    {
        var application = await uploads.FindApplicationAsync(applicationId, ct);
        if (application is null || application.JobSeekerId != seekerId)
        {
            return Result.Failure<StoredFileRef>(
                "application.not_found", "That application does not exist.");
        }

        if (application.IsWithdrawn)
        {
            return Result.Failure<StoredFileRef>(
                "application.withdrawn", "This application has been withdrawn.");
        }

        var existing = await uploads.CountApplicationFilesAsync(applicationId, ct);
        if (existing >= MaxFilesPerApplication)
        {
            return Result.Failure<StoredFileRef>(
                "file.limit_reached", $"An application can hold {MaxFilesPerApplication} files.");
        }

        var result = await UploadPipeline.RunAsync(FileKind.ProjectAttachment, payload, store, scanner, ct);
        if (result.IsFailure)
        {
            return result;
        }

        uploads.AddApplicationFile(new ApplicationFile
        {
            ApplicationId = applicationId,
            FileUrl = result.Value.Key,
            FileName = result.Value.FileName,
            CreatedAt = clock.UtcNow,
        });

        await unitOfWork.SaveChangesAsync(ct);
        return result;
    }
}

public class UploadCompanyDocumentHandler(
    IUploadRepository uploads,
    IFileStore store,
    IVirusScanner scanner,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<StoredFileRef>> HandleAsync(
        long companyId, CompanyDocumentType documentType, UploadPayload payload, CancellationToken ct = default)
    {
        var company = await uploads.FindCompanyAsync(companyId, ct);
        if (company is null)
        {
            return Result.Failure<StoredFileRef>("company.not_found", "That company does not exist.");
        }

        // Deliberately available before verification: uploading these documents
        // is how a company gets verified in the first place.
        var result = await UploadPipeline.RunAsync(FileKind.CompanyDocument, payload, store, scanner, ct);
        if (result.IsFailure)
        {
            return result;
        }

        uploads.AddCompanyDocument(
            new CompanyDocument(companyId, documentType, result.Value.Key, clock.UtcNow));

        await unitOfWork.SaveChangesAsync(ct);
        return result;
    }
}
