using System.Text.Json;
using Microsoft.Extensions.Options;
using Wasta.Application.Features.Files;

namespace Wasta.Infrastructure.Files;

public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>Where uploads land. Outside the content root, so files are never statically served.</summary>
    public string RootPath { get; set; } = Path.Combine(Path.GetTempPath(), "wasta-uploads");

    public int SignedUrlMinutes { get; set; } = 10;
}

/// <summary>
/// Filesystem-backed storage for development and self-hosting.
///
/// The uploader's filename is never part of the path. A key is generated
/// server-side, so a name containing "../" cannot escape the root no matter
/// what sanitising missed. The original name travels in a sidecar and is used
/// only to label the download.
/// </summary>
public sealed class LocalFileStore(IOptions<FileStorageOptions> options) : IFileStore
{
    private readonly string _root = options.Value.RootPath;

    public async Task<StoredFileRef> SaveAsync(
        FileKind kind, string fileName, string contentType, Stream content, CancellationToken ct = default)
    {
        var safeName = FileValidation.SanitiseFileName(fileName);
        var now = DateTimeOffset.UtcNow;

        // Date-partitioned so a directory never accumulates millions of entries.
        var key = $"{kind.ToString().ToLowerInvariant()}/{now:yyyy/MM}/{Guid.NewGuid():N}";
        var path = ResolvePath(key);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        long length;
        await using (var file = File.Create(path))
        {
            await content.CopyToAsync(file, ct);
            length = file.Length;
        }

        var metadata = JsonSerializer.Serialize(new StoredMetadata(safeName, contentType, length));
        await File.WriteAllTextAsync(path + ".meta", metadata, ct);

        return new StoredFileRef(key, safeName, contentType, length);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var path = ResolvePath(key);

        return Task.FromResult<Stream?>(
            File.Exists(path) ? File.OpenRead(path) : null);
    }

    public async Task<StoredFileRef?> GetMetadataAsync(string key, CancellationToken ct = default)
    {
        var path = ResolvePath(key) + ".meta";
        if (!File.Exists(path))
        {
            return null;
        }

        var metadata = JsonSerializer.Deserialize<StoredMetadata>(await File.ReadAllTextAsync(path, ct));
        return metadata is null
            ? null
            : new StoredFileRef(key, metadata.FileName, metadata.ContentType, metadata.Length);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = ResolvePath(key);

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        if (File.Exists(path + ".meta"))
        {
            File.Delete(path + ".meta");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves a key under the root and refuses anything that climbs out of it.
    /// Keys are server-generated, so this should be unreachable - which is
    /// precisely why it is here rather than assumed.
    /// </summary>
    private string ResolvePath(string key)
    {
        var root = Path.GetFullPath(_root);
        var combined = Path.GetFullPath(Path.Combine(root, key));

        if (!combined.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && combined != root)
        {
            throw new InvalidOperationException("Resolved file path escaped the storage root.");
        }

        return combined;
    }

    private sealed record StoredMetadata(string FileName, string ContentType, long Length);
}
