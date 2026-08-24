using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wasta.Application.Features.Files;
using Wasta.Infrastructure.Files;

namespace Wasta.Api.IntegrationTests;

/// <summary>
/// Speaks the clamd wire protocol back at the scanner.
///
/// What ClamAV detects is ClamAV's problem. What these assert is ours: that the
/// INSTREAM framing is right, that the file arrives byte-for-byte, that a reply
/// we cannot read is never mistaken for "clean", and that an unreachable
/// scanner fails closed.
/// </summary>
public class ClamAvVirusScannerTests
{
    private static ClamAvVirusScanner ScannerFor(int port, int timeoutSeconds = 10) =>
        new(Options.Create(new ClamAvOptions
        {
            Enabled = true,
            Host = "127.0.0.1",
            Port = port,
            TimeoutSeconds = timeoutSeconds,
        }), NullLogger<ClamAvVirusScanner>.Instance);

    [Fact]
    public async Task A_clean_reply_is_clean_and_the_file_arrives_byte_for_byte()
    {
        // Larger than one chunk, so the multi-chunk path is the one under test.
        var payload = new byte[(64 * 1024) + 517];
        Random.Shared.NextBytes(payload);

        using var clamd = new FakeClamd("stream: OK");

        var result = await ScannerFor(clamd.Port).ScanAsync(new MemoryStream(payload));

        Assert.True(result.IsClean);
        Assert.Equal("zINSTREAM", clamd.Command);
        Assert.Equal(payload, await clamd.Received);
    }

    [Fact]
    public async Task A_FOUND_reply_rejects_the_file_without_naming_the_signature()
    {
        using var clamd = new FakeClamd("stream: Win.Test.EICAR_HDB-1 FOUND");

        var result = await ScannerFor(clamd.Port).ScanAsync(new MemoryStream([1, 2, 3]));

        Assert.False(result.IsClean);

        // The detail reaches whoever uploaded the file. Naming the detection
        // tells someone probing the scanner which samples get through.
        Assert.DoesNotContain("EICAR", result.Detail ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_size_limit_error_is_never_mistaken_for_clean()
    {
        // clamd reports its own stream limit as an ERROR. Treating anything that
        // is not FOUND as clean would store the one file too big to inspect.
        using var clamd = new FakeClamd("INSTREAM size limit exceeded. ERROR");

        await Assert.ThrowsAsync<VirusScannerUnavailableException>(
            () => ScannerFor(clamd.Port).ScanAsync(new MemoryStream([1, 2, 3])));
    }

    [Fact]
    public async Task An_unreachable_scanner_fails_closed()
    {
        // Bind then release, so the port is real but nothing is listening.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var deadPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        // The whole point of the class: down must not mean "clean".
        await Assert.ThrowsAsync<VirusScannerUnavailableException>(
            () => ScannerFor(deadPort).ScanAsync(new MemoryStream([1, 2, 3])));
    }

    [RequiresClamd]
    public async Task Real_clamd_detects_EICAR_and_passes_a_clean_file()
    {
        var port = int.Parse(Environment.GetEnvironmentVariable(RequiresClamdAttribute.PortVariable)!);
        var scanner = ScannerFor(port, timeoutSeconds: 60);

        // Assembled from fragments so this source file does not itself trip a
        // desktop scanner and get quarantined out of the repo.
        var eicar = string.Concat(
            "X5O!P%@AP[4\\PZX54(P^)7CC)7}$",
            "EICAR", "-STANDARD-", "ANTIVIRUS-", "TEST-FILE!", "$H+H*");

        var infected = await scanner.ScanAsync(new MemoryStream(Encoding.ASCII.GetBytes(eicar)));
        Assert.False(infected.IsClean);

        var clean = await scanner.ScanAsync(
            new MemoryStream(Encoding.ASCII.GetBytes("An entirely ordinary CV.")));
        Assert.True(clean.IsClean);
    }

    /// <summary>Accepts one connection, reassembles the INSTREAM body, then answers.</summary>
    private sealed class FakeClamd : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task<byte[]> _session;

        public FakeClamd(string reply)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();

            _session = Task.Run(async () =>
            {
                using var client = await _listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();

                var command = new List<byte>();
                var one = new byte[1];
                while (await stream.ReadAsync(one) > 0 && one[0] != 0)
                {
                    command.Add(one[0]);
                }

                Command = Encoding.ASCII.GetString(command.ToArray());

                var body = new List<byte>();
                var header = new byte[4];
                while (true)
                {
                    await stream.ReadExactlyAsync(header);
                    var length = BinaryPrimitives.ReadInt32BigEndian(header);
                    if (length == 0)
                    {
                        break;
                    }

                    var chunk = new byte[length];
                    await stream.ReadExactlyAsync(chunk);
                    body.AddRange(chunk);
                }

                await stream.WriteAsync(Encoding.ASCII.GetBytes(reply + "\0"));
                await stream.FlushAsync();

                return body.ToArray();
            });
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public string? Command { get; private set; }

        public Task<byte[]> Received => _session;

        public void Dispose() => _listener.Stop();
    }
}

/// <summary>
/// Skips rather than passes when no real clamd is configured. A test that
/// quietly returns early reports green, and a green run proving nothing is
/// worse than no run at all.
/// </summary>
public sealed class RequiresClamdAttribute : FactAttribute
{
    public const string PortVariable = "WASTA_CLAMD_PORT";

    public RequiresClamdAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PortVariable)))
        {
            Skip = $"Set {PortVariable} to a running clamd to exercise the real protocol.";
        }
    }
}
