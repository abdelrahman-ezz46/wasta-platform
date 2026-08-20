namespace Wasta.Application.Features.Files;

/// <summary>
/// What an upload must satisfy before it is written anywhere.
///
/// The signature check is the point. A client controls the filename and the
/// Content-Type header completely, so neither is evidence of anything: an
/// executable renamed to cv.pdf and sent as application/pdf passes both. Only
/// the leading bytes say what a file actually is.
/// </summary>
public static class FileValidation
{
    public const long MaxCvBytes = 5 * 1024 * 1024;
    public const long MaxAttachmentBytes = 15 * 1024 * 1024;
    public const long MaxDocumentBytes = 10 * 1024 * 1024;

    public static long MaxBytesFor(FileKind kind) => kind switch
    {
        FileKind.Cv => MaxCvBytes,
        FileKind.ProjectAttachment => MaxAttachmentBytes,
        FileKind.CompanyDocument => MaxDocumentBytes,
        _ => MaxCvBytes,
    };

    private static readonly byte[] Pdf = "%PDF"u8.ToArray();
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];

    // DOCX, PPTX and XLSX are zip containers, so they share a signature with
    // every other zip. That is as far as a magic-byte check can go; anything
    // more would mean parsing the archive.
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];

    private static readonly Dictionary<FileKind, string[]> Allowed = new()
    {
        [FileKind.Cv] = ["application/pdf"],
        [FileKind.ProjectAttachment] =
        [
            "application/pdf",
            "image/png",
            "image/jpeg",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ],
        [FileKind.CompanyDocument] = ["application/pdf", "image/png", "image/jpeg"],
    };

    public static IReadOnlyList<string> AllowedContentTypes(FileKind kind) =>
        Allowed.TryGetValue(kind, out var types) ? types : [];

    public static FileValidationResult Validate(
        FileKind kind, string? fileName, string? contentType, long length, ReadOnlySpan<byte> leadingBytes)
    {
        if (length <= 0)
        {
            return FileValidationResult.Invalid("file.empty", "The file is empty.");
        }

        var max = MaxBytesFor(kind);
        if (length > max)
        {
            return FileValidationResult.Invalid(
                "file.too_large", $"That file is larger than the {max / (1024 * 1024)} MB limit.");
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return FileValidationResult.Invalid("file.name_required", "A file name is required.");
        }

        var declared = contentType?.Split(';')[0].Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(declared) || !AllowedContentTypes(kind).Contains(declared))
        {
            return FileValidationResult.Invalid(
                "file.type_not_allowed",
                $"Allowed types: {string.Join(", ", AllowedContentTypes(kind))}.");
        }

        if (!MatchesSignature(declared, leadingBytes))
        {
            return FileValidationResult.Invalid(
                "file.content_mismatch",
                "The file's contents do not match its declared type.");
        }

        return FileValidationResult.Valid;
    }

    private static bool MatchesSignature(string contentType, ReadOnlySpan<byte> bytes) => contentType switch
    {
        "application/pdf" => bytes.StartsWith(Pdf),
        "image/png" => bytes.StartsWith(Png),
        "image/jpeg" => bytes.StartsWith(Jpeg),
        _ => bytes.StartsWith(Zip),
    };

    /// <summary>
    /// Strips everything but a bare name and extension. The uploader's filename
    /// is never used as a storage path - it is echoed back in downloads, so a
    /// name carrying "../" or a control character has to be flattened first.
    /// </summary>
    public static string SanitiseFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));

        var cleaned = new string(name
            .Where(c => !char.IsControl(c) && !Path.GetInvalidFileNameChars().Contains(c))
            .ToArray())
            .Trim()
            .TrimStart('.');

        if (cleaned.Length == 0)
        {
            cleaned = "upload";
        }

        return cleaned.Length > 200 ? cleaned[^200..] : cleaned;
    }
}
