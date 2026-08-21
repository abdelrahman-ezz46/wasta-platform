using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wasta.Application.Abstractions;
using Wasta.Infrastructure.Files;
using Wasta.Infrastructure.Localization;
using Wasta.Infrastructure.Notifications;
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
        services.AddScoped<IAssessmentRepository, AssessmentRepository>();
        services.AddScoped<IAttemptRepository, AttemptRepository>();
        services.AddScoped<IJobPostRepository, JobPostRepository>();
        services.AddScoped<IJobQueries, JobQueries>();
        services.AddScoped<IJobApplicationRepository, JobApplicationRepository>();
        services.AddScoped<ITalentPoolQueries, TalentPoolQueries>();
        services.AddScoped<ICreditQueries, CreditQueries>();
        services.AddScoped<ICreditRepository, CreditRepository>();
        services.AddScoped<ICompanyRepositoryForAdmin, CompanyRepositoryForAdmin>();
        services.AddScoped<Application.Features.TalentPool.IUnlockService, UnlockService>();
        services.AddScoped<IUploadRepository, UploadRepository>();
        services.AddScoped<IAdminContentRepository, AdminContentRepository>();
        services.AddScoped<IAdminContentQueries, AdminContentRepository>();
        services.AddScoped<IAccountTokenRepository, AccountTokenRepository>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<Application.Features.Auth.IPersonalDataQueries, PersonalDataQueries>();
        services.AddScoped<Application.Features.Auth.IPersonalDataEraser, PersonalDataEraser>();

        services.Configure<Application.Features.Auth.AccountLinkOptions>(
            configuration.GetSection(Application.Features.Auth.AccountLinkOptions.SectionName));

        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));
        services.AddSingleton<Application.Features.Files.IFileStore, LocalFileStore>();
        services.AddSingleton<Application.Features.Files.IFileUrlSigner, HmacFileUrlSigner>();

        // Placeholder. The startup check warns on every boot that uploads are
        // not actually being scanned.
        services.AddSingleton<Application.Features.Files.IVirusScanner, NoOpVirusScanner>();
        services.AddHostedService<VirusScannerStartupCheck>();

        services.Configure<NotificationDispatcherOptions>(
            configuration.GetSection(NotificationDispatcherOptions.SectionName));

        services.AddScoped<Application.Features.Notifications.INotificationService, NotificationService>();
        services.AddScoped<Application.Features.Notifications.INotificationRecipients, NotificationRecipients>();
        services.AddScoped<Application.Features.Notifications.INotificationQueries, NotificationQueries>();
        services.AddScoped<Application.Features.Notifications.INotificationRepository, NotificationRepository>();

        // Writes to the log rather than sending. The startup check says so.
        services.Configure<LoggingNotificationSenderOptions>(
            configuration.GetSection(LoggingNotificationSenderOptions.SectionName));
        services.AddSingleton<Application.Features.Notifications.INotificationSender, LoggingNotificationSender>();
        services.AddHostedService<NotificationSenderStartupCheck>();
        services.AddHostedService<NotificationDispatcher>();

        services.AddMemoryCache();
        services.AddSingleton<Application.Features.Localization.ILocalizer, CachedLocalizer>();
        services.AddScoped<Application.Features.Localization.IReferenceDataQueries, ReferenceDataQueries>();

        services.Configure<Application.Features.Assessments.AssessmentOptions>(
            configuration.GetSection(Application.Features.Assessments.AssessmentOptions.SectionName));

        return services;
    }
}
