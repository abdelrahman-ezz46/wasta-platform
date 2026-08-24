using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Wasta.Application.Features.Files;

namespace Wasta.WebApi;

/// <summary>
/// The malware scanner failing is a real incident, but it is not the caller's
/// fault and it is not permanent. 503 says "try again later", which is true;
/// a bare 500 would read as a bug in the upload itself.
///
/// The message is deliberately vague to the caller. Naming the scanner, its
/// host or its port tells whoever is probing the upload endpoint exactly what
/// is sitting behind it.
/// </summary>
public sealed class ScannerUnavailableExceptionHandler(
    ILogger<ScannerUnavailableExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not VirusScannerUnavailableException scanner)
        {
            return false;
        }

        // Error, not information: uploads are refused for as long as this lasts,
        // and somebody needs to know.
        logger.LogError(scanner, "Upload refused because the malware scanner is unavailable.");

        var problem = new ProblemDetails
        {
            Title = "Not available",
            Detail = "Uploads are temporarily unavailable. Please try again shortly.",
            Status = StatusCodes.Status503ServiceUnavailable,
            Extensions = { ["code"] = "file.scanner_unavailable" },
        };

        httpContext.Response.StatusCode = problem.Status.Value;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
