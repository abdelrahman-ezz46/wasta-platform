using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Wasta.Infrastructure.Persistence;

namespace Wasta.Api.IntegrationTests;

/// <summary>
/// Boots the real API against a throwaway PostgreSQL container.
///
/// Deliberately not the in-memory provider: it ignores column types and does
/// not enforce unique indexes, so the two things these tests exist to prove -
/// that jsonb columns work and that the unique constraints actually hold -
/// would pass against it whether or not the schema was right.
/// </summary>
public sealed class WastaApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _uploadRoot =
        Path.Combine(Path.GetTempPath(), "wasta-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Captures outbound mail so tests can read emailed links.</summary>
    public CapturingNotificationSender Sender { get; } = new();

    public const string AdminEmail = "admin@wasta.test";
    public const string AdminPassword = "AdminPassw0rd123";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("wasta_test")
        .WithUsername("postgres")
        .WithPassword("test_only")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WastaDbContext>();
        await db.Database.MigrateAsync();

        // The AI modules keep their own DbContexts and their own migrations,
        // sharing this database. All three histories live in one
        // __EFMigrationsHistory table, which works because their migration ids
        // are timestamped and therefore distinct.
        await scope.ServiceProvider
            .GetRequiredService<Wasta.CareerCoach.Data.CoachDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider
            .GetRequiredService<Wasta.SupportChat.Data.SupportChatDbContext>().Database.MigrateAsync();

        // Tests run against the same seed the dev host uses, so a test passing
        // here means the shipped reference data is coherent too.
        await DatabaseSeeder.SeedAsync(db);

        await DatabaseSeeder.SeedAdminAsync(
            db,
            scope.ServiceProvider.GetRequiredService<Wasta.Application.Abstractions.IPasswordHasher>(),
            AdminEmail,
            AdminPassword);
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();

        if (Directory.Exists(_uploadRoot))
        {
            Directory.Delete(_uploadRoot, recursive: true);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not Development. The host seeds itself on startup in Development, and
        // starting the host is what materialises Services - so the app would
        // seed against a database this fixture has not migrated yet. Migrating
        // and seeding is this fixture's job, in that order.
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:Wasta", _postgres.GetConnectionString());

        // A fixed test key. Never a fallback in the app itself - the host throws
        // on a missing key precisely so a predictable one cannot ship.
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-not-a-real-secret-000");
        // The auth limit is raised well clear of the suite: every test registers
        // or logs in, and they all arrive from one address. The upload limit is
        // left low on purpose - it partitions per user, so one test can exhaust
        // its own budget without touching anyone else's.
        builder.UseSetting("RateLimits:AuthPerMinute", "10000");
        builder.UseSetting("RateLimits:UnlockPerMinute", "10000");
        builder.UseSetting("RateLimits:UploadPerFiveMinutes", "5");

        builder.UseSetting("FileStorage:RootPath", _uploadRoot);

        // The dispatcher runs on a timer. Tests drive a batch directly instead,
        // so a delivery assertion is deterministic rather than a race.
        builder.UseSetting("Notifications:Enabled", "false");

        builder.UseSetting("Jwt:Issuer", "wasta");
        builder.UseSetting("AccountLinks:BaseUrl", "https://app.wasta.test");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<Wasta.Application.Features.Notifications.INotificationSender>();
            services.AddSingleton<Wasta.Application.Features.Notifications.INotificationSender>(Sender);
        });
        builder.UseSetting("Jwt:Audience", "wasta-api");
    }
}

[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<WastaApiFactory>;
