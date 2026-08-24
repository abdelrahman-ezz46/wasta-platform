namespace Wasta.Application.Features.Files;

public enum FileKind
{
    /// <summary>A seeker's CV. PDF only, 5 MB, per the profile screen.</summary>
    Cv = 1,

    /// <summary>Work attached to an application: slides, documents, images.</summary>
    ProjectAttachment = 2,

    /// <summary>Commercial register, tax card, or similar, for company verification.</summary>
    CompanyDocument = 3,
}

public sealed record StoredFileRef(string Key, string FileName, string ContentType, long Length);

public sealed record FileValidationResult(bool IsValid, string? Code, string? Message)
{
    public static readonly FileValidationResult Valid = new(true, null, null);

    public static FileValidationResult Invalid(string code, string message) => new(false, code, message);
}

/// <summary>
/// Where bytes actually live. The local implementation is for development; a
/// deployment swaps in object storage without anything above this changing.
/// </summary>
public interface IFileStore
{
    Task<StoredFileRef> SaveAsync(
        FileKind kind, string fileName, string contentType, Stream content, CancellationToken ct = default);

    Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default);

    /// <summary>Original name, type and size. Needed to label a download correctly.</summary>
    Task<StoredFileRef?> GetMetadataAsync(string key, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);
}

/// <summary>
/// Grants time-limited access to one stored file. Uploaded files must never be
/// reachable by guessing a path: a CV is personal data, and a verification
/// document is a company's legal paperwork.
/// </summary>
public interface IFileUrlSigner
{
    string CreateToken(string key, DateTimeOffset expiresAt);

    bool IsValid(string key, string token);

    TimeSpan DefaultLifetime { get; }
}

public sealed record ScanResult(bool IsClean, string? Detail)
{
    public static readonly ScanResult Clean = new(true, null);
}

/// <summary>
/// Malware scanning for uploaded files. Deliberately an interface with no real
/// implementation in this repo: shipping something that returns "clean" without
/// scanning would be worse than having nothing, because it would look handled.
/// </summary>
/// <summary>
/// Thrown when a scanner cannot be reached, or answers with something we cannot
/// read.
///
/// Deliberately an exception rather than a not-clean <see cref="ScanResult"/>.
/// "The scanner is down" and "this file is malware" are different facts, and
/// reporting the first as the second tells a student their CV is infected when
/// nothing ever looked at it. A genuine fault throws; an expected outcome is a
/// Result.
/// </summary>
public sealed class VirusScannerUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

public interface IVirusScanner
{
    Task<ScanResult> ScanAsync(Stream content, CancellationToken ct = default);

    /// <summary>False for the no-op, which the host warns about on every boot.</summary>
    bool IsRealScanner { get; }
}
