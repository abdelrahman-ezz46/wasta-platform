using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Wasta.Domain.Common;

namespace Wasta.WebApi;

/// <summary>
/// A broken business rule is the caller's problem, not a server fault. Without
/// this, a DomainException escapes as a 500 - which tells the client nothing,
/// and pages whoever is on call for what is really a bad request.
/// </summary>
public sealed class DomainExceptionHandler(ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainException domain)
        {
            return false;
        }

        // Logged at information: this is an expected outcome, not an incident.
        logger.LogInformation("Domain rule rejected the request: {Code}", domain.Code);

        var problem = new ProblemDetails
        {
            Title = "Bad request",
            Detail = domain.Message,
            Status = StatusCodes.Status400BadRequest,
            Extensions = { ["code"] = domain.Code },
        };

        httpContext.Response.StatusCode = problem.Status.Value;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
