using Wasta.Application.Abstractions;
using Wasta.CareerCoach.Services;

namespace Wasta.WebApi.Integration;

/// <summary>
/// The real trigger, replacing the no-op once the AI modules are registered.
/// Enqueues only; generation happens on the module's own background worker.
/// </summary>
public sealed class PlatformCoachPlanTrigger(CoachPlanTrigger trigger) : ICoachPlanTrigger
{
    public Task EnqueueAsync(long seekerId, long attemptId, CancellationToken ct = default)
    {
        var studentId = PlatformIds.ToModuleId(seekerId, "Seeker id");
        var attempt = PlatformIds.ToModuleId(attemptId, "Attempt id");

        // Attempt id doubles as the score id: the platform keys a score by its
        // attempt, so there is no separate number to pass.
        return trigger.EnqueueGenerationAsync(studentId, attempt, attempt, ct);
    }
}
