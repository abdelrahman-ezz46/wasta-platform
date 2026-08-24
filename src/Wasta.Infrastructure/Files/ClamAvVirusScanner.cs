using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wasta.Application.Features.Files;

namespace Wasta.Infrastructure.Files;

public sealed class ClamAvOptions
{
    public const string SectionName = "VirusScanning";

    /// <summary>
    /// Off by default. Turning it on without a reachable clamd stops uploads
    /// entirely - which is the intended failure direction, but not a surprise
    /// anyone should get by accident.
    /// </summary>
    public bool Enabled { get; set; }

    public string Host { get; set; } = "clamav";

    public int Port { get; set; } = 3310;

    /// <summary>
    /// Generous on purpose. A cold clamd spends the better part of a minute
    /// loading signatures and refuses connections until it has.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Must stay under clamd's own StreamMaxLength, or clamd closes the
    /// connection part-way through a send and the reply never arrives.
    /// </summary>
    public int ChunkBytes { get; set; } = 64 * 1024;
}

/// <summary>
/// Streams uploads to clamd over TCP using INSTREAM.
///
/// Fails CLOSED: if clamd is unreachable, uploads fail rather than being stored
/// unscanned. A scanner that waves files through when it is down is the no-op
/// scanner with extra steps, and the whole point of this class is that students
/// upload CVs which strangers download.
/// </summary>
public sealed class ClamAvVirusScanner(
    IOptions<ClamAvOptions> options, ILogger<ClamAvVirusScanner> logger) : IVirusScanner
{
    public bool IsRealScanner => true;

    public async Task<ScanResult> ScanAsync(Stream content, CancellationToken ct = default)
    {
        var settings = options.Value;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        try
        {
            return await ScanInternalAsync(content, settings, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The caller did not cancel, so this is our timeout, not their abort.
            throw new VirusScannerUnavailableException(
                $"clamd at {settings.Host}:{settings.Port} did not answer within "
                + $"{settings.TimeoutSeconds}s.");
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            throw new VirusScannerUnavailableException(
                $"Could not reach clamd at {settings.Host}:{settings.Port}.", ex);
        }
    }

    private async Task<ScanResult> ScanInternalAsync(
        Stream content, ClamAvOptions settings, CancellationToken ct)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(settings.Host, settings.Port, ct);

        await using var socket = client.GetStream();

        // The 'z' prefix tells clamd the command is NUL-terminated and that it
        // should terminate its reply the same way. Unambiguous to frame, unlike
        // the newline form.
        await socket.WriteAsync(Encoding.ASCII.GetBytes("zINSTREAM\0"), ct);

        var chunk = new byte[settings.ChunkBytes];
        var header = new byte[4];

        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            BinaryPrimitives.WriteInt32BigEndian(header, read);
            await socket.WriteAsync(header, ct);
            await socket.WriteAsync(chunk.AsMemory(0, read), ct);
        }

        // A zero-length chunk is how INSTREAM says "that is the whole file".
        BinaryPrimitives.WriteInt32BigEndian(header, 0);
        await socket.WriteAsync(header, ct);
        await socket.FlushAsync(ct);

        var reply = await ReadReplyAsync(socket, ct);

        return Interpret(reply, settings);
    }

    private ScanResult Interpret(string reply, ClamAvOptions settings)
    {
        // clamd answers "stream: OK" or "stream: <signature> FOUND", and puts
        // ERROR on anything it could not do - including a file over its own
        // size limit, which must not be mistaken for a clean result.
        if (reply.EndsWith("OK", StringComparison.Ordinal))
        {
            return ScanResult.Clean;
        }

        if (reply.EndsWith("FOUND", StringComparison.Ordinal))
        {
            var signature = reply
                .Replace("stream:", string.Empty, StringComparison.Ordinal)
                .Replace("FOUND", string.Empty, StringComparison.Ordinal)
                .Trim();

            // The signature name is logged, not returned. It goes to whoever
            // uploaded the file, and naming the detection tells someone probing
            // the scanner exactly which sample got through and which did not.
            logger.LogWarning("clamd rejected an upload: {Signature}", signature);

            return new ScanResult(false, "Rejected by the malware scanner.");
        }

        throw new VirusScannerUnavailableException(
            $"clamd at {settings.Host}:{settings.Port} answered with '{reply}'.");
    }

    private static async Task<string> ReadReplyAsync(Stream socket, CancellationToken ct)
    {
        var buffer = new byte[256];
        var builder = new StringBuilder();

        int read;
        while ((read = await socket.ReadAsync(buffer, ct)) > 0)
        {
            var text = Encoding.ASCII.GetString(buffer, 0, read);
            var nul = text.IndexOf('\0', StringComparison.Ordinal);

            if (nul >= 0)
            {
                builder.Append(text[..nul]);
                break;
            }

            builder.Append(text);
        }

        return builder.ToString().Trim();
    }
}
