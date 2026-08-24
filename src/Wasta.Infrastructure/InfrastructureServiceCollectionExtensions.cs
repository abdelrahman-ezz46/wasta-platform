using Microsoft.EntityFrameworkCore;
using Amazon.SimpleEmailV2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
            // Its own history table. The AI modules keep their own DbContexts in
            // this same database and do NOT use the snake_case convention, so
            // sharing __EFMigrationsHistory would mean one table whose columns
            // are snake_case to us and PascalCase to them - which fails at
            // runtime the moment the second context migrates.
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable(WastaDbContext.MigrationsHistoryTable))
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
        services.AddScoped<ILoggerAdapter, LoggerAdapter>();

        // Replaced by the real trigger when the AI modules are wired in.
        services.TryAddScoped<ICoachPlanTrigger, NoCoachPlanTrigger>();
        services.AddScoped<IAccountTokenRepository, AccountTokenRepository>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<Application.Features.Auth.IPersonalDataQueries, PersonalDataQueries>();
        services.AddScoped<Application.Features.Auth.IPersonalDataEraser, PersonalDataEraser>();

        services.Configure<Application.Features.Auth.AccountLinkOptions>(
            configuration.GetSection(Application.Features.Auth.AccountLinkOptions.SectionName));

        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));
        services.AddSingleton<Application.Features.Files.IFileStore, LocalFileStore>();
        services.AddSingleton<Application.Features.Files.IFileUrlSigner, HmacFileUrlSigner>();

        services.Configure<ClamAvOptions>(configuration.GetSection(ClamAvOptions.SectionName));

        // Real scanning is opt-in, and enabling it without a reachable clamd
        // stops uploads rather than letting them through unscanned. Left off,
        // the no-op stands in and the startup check says so on every boot.
        if (configuration.GetValue<bool>($"{ClamAvOptions.SectionName}:Enabled"))
        {
            services.AddSingleton<Application.Features.Files.IVirusScanner, ClamAvVirusScanner>();
        }
        else
        {
            services.AddSingleton<Application.Features.Files.IVirusScanner, NoOpVirusScanner>();
        }

        services.AddHostedService<VirusScannerStartupCheck>();

        services.Configure<NotificationDispatcherOptions>(
            configuration.GetSection(NotificationDispatcherOptions.SectionName));

        services.AddScoped<Application.Features.Notifications.INotificationService, NotificationService>();
        services.AddScoped<Application.Features.Notifications.INotificationRecipients, NotificationRecipients>();
        services.AddScoped<Application.Features.Notifications.INotificationQueries, NotificationQueries>();
        services.AddScoped<Application.Features.Notifications.INotificationRepository, NotificationRepository>();

        services.Configure<LoggingNotificationSenderOptions>(
            configuration.GetSection(LoggingNotificationSenderOptions.SectionName));
        services.Configure<SesNotificationSenderOptions>(
            configuration.GetSection(SesNotificationSenderOptions.SectionName));

        if (configuration.GetValue<bool>($"{SesNotificationSenderOptions.SectionName}:Enabled"))
        {
            var email = configuration.GetSection(SesNotificationSenderOptions.SectionName)
                .Get<SesNotificationSenderOptions>() ?? new SesNotificationSenderOptions();

            // Fail at startup rather than at the first password reset. An unset
            // value binds as "" and would otherwise reach SES as an empty sender.
            if (string.IsNullOrWhiteSpace(email.FromAddress))
            {
                throw new InvalidOperationException(
                    "Email:Enabled is on but Email:FromAddress is empty. Set it to an SES-verified "
                    + "sender identity.");
            }

            services.AddSingleton<IAmazonSimpleEmailServiceV2>(_ => new AmazonSimpleEmailServiceV2Client(
                Amazon.RegionEndpoint.GetBySystemName(email.Region)));
            services.AddSingleton<Application.Features.Notifications.INotificationSender, SesNotificationSender>();
        }
        else
        {
            // Writes to the log rather than sending. The startup check says so.
            services.AddSingleton<Application.Features.Notifications.INotificationSender, LoggingNotificationSender>();
        }
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
