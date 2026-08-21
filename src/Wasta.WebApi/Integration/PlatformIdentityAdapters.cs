using System.Security.Claims;
using Wasta.WebApi.Auth;
using CoachDomain = Wasta.CareerCoach.Domain;
using ChatDomain = Wasta.SupportChat.Domain;

namespace Wasta.WebApi.Integration;

/// <summary>
/// Resolves the caller's seeker id for both AI modules.
///
/// The two modules declare their own one-method interface rather than sharing
/// one, deliberately - they have no real dependency on each other. One class
/// satisfies both here because in this host they mean the same thing.
/// </summary>
public sealed class PlatformStudentAccessor : CoachDomain.ICurrentStudentAccessor, ChatDomain.ICurrentStudentAccessor
{
    public int? GetStudentId(ClaimsPrincipal user) => PlatformIds.TryToModuleId(user.SeekerId());
}
