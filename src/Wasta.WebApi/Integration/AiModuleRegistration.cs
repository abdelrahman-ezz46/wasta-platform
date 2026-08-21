using Microsoft.EntityFrameworkCore;
using Wasta.Application.Abstractions;
using Wasta.CareerCoach;
using Wasta.SupportChat;
using CoachDomain = Wasta.CareerCoach.Domain;
using ChatDomain = Wasta.SupportChat.Domain;

namespace Wasta.WebApi.Integration;

public static class AiModuleRegistration
{
    /// <summary>
    /// Wires the two AI modules into the platform.
    ///
    /// The ports go in first, deliberately: SupportChat registers a no-op job
    /// provider with TryAdd, so a real one only wins if it is already there.
    ///
    /// Both modules can be switched off from one flag. They are add-ons to the
    /// results page and the help widget, and neither is worth taking the
    /// platform down for.
    /// </summary>
    public static IServiceCollection AddWastaAiModules(
        this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        services.AddScoped<PlatformStudentAccessor>();
        services.AddScoped<CoachDomain.ICurrentStudentAccessor>(
            sp => sp.GetRequiredService<PlatformStudentAccessor>());
        services.AddScoped<ChatDomain.ICurrentStudentAccessor>(
            sp => sp.GetRequiredService<PlatformStudentAccessor>());

        services.AddScoped<CoachDomain.IAssessmentDataProvider, PlatformAssessmentDataProvider>();
        services.AddScoped<CoachDomain.IAuditLogWriter, PlatformAuditLogWriter>();
        services.AddScoped<ChatDomain.IJobListingProvider, PlatformJobListingProvider>();

        services.AddCareerCoach(configuration, options => options.UseNpgsql(connectionString));
        services.AddSupportChat(configuration, options => options.UseNpgsql(connectionString));

        // Replaces the no-op registered by Infrastructure.
        services.AddScoped<ICoachPlanTrigger, PlatformCoachPlanTrigger>();

        return services;
    }
}
