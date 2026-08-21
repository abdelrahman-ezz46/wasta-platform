using System.Text.Json;
using Wasta.Application.Abstractions;
using Wasta.CareerCoach.Domain;

namespace Wasta.WebApi.Integration;

/// <summary>
/// Sends the Career Coach's audit entries to the platform's own audit log,
/// rather than letting the module keep a second one nobody reads.
/// </summary>
public sealed class PlatformAuditLogWriter(IAuditWriter audit, IUnitOfWork unitOfWork, IClock clock)
    : IAuditLogWriter
{
    public async Task WriteAsync(string action, string? actorId, string details, CancellationToken ct)
    {
        var parsedActor = long.TryParse(actorId, out var id) ? id : (long?)null;

        audit.Write(
            parsedActor,
            $"coach.{action}",
            "student_coach_plan",
            actorId ?? "unknown",
            new { details },
            clock.UtcNow);

        // Written immediately: this is called from the module's own request
        // handling, which has no unit of work of ours to ride along with.
        await unitOfWork.SaveChangesAsync(ct);
    }
}
