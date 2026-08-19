using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Wasta.Application.Features.Assessments;
using Wasta.Application.Features.Auth;

namespace Wasta.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers use-case handlers and their validators. Handlers are plain
    /// classes resolved directly - no mediator, because a dispatcher would add
    /// indirection without removing any work here.
    /// </summary>
    public static IServiceCollection AddWastaApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<LoginValidator>();

        services.AddScoped<RegisterSeekerHandler>();
        services.AddScoped<RegisterCompanyHandler>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<RefreshHandler>();

        services.AddScoped<StartAttemptHandler>();
        services.AddScoped<GetAttemptHandler>();
        services.AddScoped<SaveAnswerHandler>();
        services.AddScoped<SubmitAttemptHandler>();
        services.AddScoped<GetResultsHandler>();

        return services;
    }
}
