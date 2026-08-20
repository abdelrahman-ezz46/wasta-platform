using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wasta.Application.Features.Files;

namespace Wasta.Infrastructure.Files;

/// <summary>
/// Placeholder scanner. Reports every file clean because it does not look at
/// them.
///
/// This exists so the upload path is complete and swappable, not so the box can
/// be ticked. Students upload CVs that companies download, which is a
/// file-delivery channel between strangers - exactly the shape that needs real
/// scanning before launch. The host warns about this on every boot rather than
/// letting it pass quietly.
/// </summary>
public sealed class NoOpVirusScanner : IVirusScanner
{
    public bool IsRealScanner => false;

    public Task<ScanResult> ScanAsync(Stream content, CancellationToken ct = default) =>
        Task.FromResult(ScanResult.Clean);
}

/// <summary>Says so at startup, in the same spirit as the knowledge-base TODO warning.</summary>
public sealed class VirusScannerStartupCheck(IVirusScanner scanner, ILogger<VirusScannerStartupCheck> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (scanner.IsRealScanner)
        {
            logger.LogInformation("Uploads are being scanned by {Scanner}.", scanner.GetType().Name);
        }
        else
        {
            logger.LogWarning(
                "Uploads are NOT being scanned for malware. {Scanner} reports every file clean without "
                + "inspecting it. Wire a real scanner before accepting uploads from the public.",
                scanner.GetType().Name);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
