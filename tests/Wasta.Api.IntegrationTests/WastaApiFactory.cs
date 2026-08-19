using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

        // Tests run against the same seed the dev host uses, so a test passing
        // here means the shipped reference data is coherent too.
        await DatabaseSeeder.SeedAsync(db);
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
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
        builder.UseSetting("Jwt:Issuer", "wasta");
        builder.UseSetting("Jwt:Audience", "wasta-api");
    }
}

[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<WastaApiFactory>;
