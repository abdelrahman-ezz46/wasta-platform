using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wasta.Application.Abstractions;
using Wasta.Infrastructure.Identity;
using Wasta.Infrastructure.Persistence;
using Wasta.Infrastructure.Persistence.Repositories;

namespace Wasta.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers persistence and the adapters behind the Application layer's
    /// interfaces. The web host calls this once; nothing above Infrastructure
    /// knows EF Core is involved.
    /// </summary>
    public static IServiceCollection AddWastaInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Wasta")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Wasta is not configured. The API will not start without a database.");

        services.AddDbContext<WastaDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IUserAccountRepository, UserAccountRepository>();
        services.AddScoped<IJobSeekerRepository, JobSeekerRepository>();
        services.AddScoped<ICompanyRepository, CompanyRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAuthorizationQueries, AuthorizationQueries>();
        services.AddScoped<Application.Features.Me.IMeQueries, MeQueries>();

        return services;
    }
}
