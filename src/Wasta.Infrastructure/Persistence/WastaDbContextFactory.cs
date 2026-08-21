using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Wasta.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef` at design time. The connection string here never
/// reaches a running app - it points at the local docker-compose Postgres so
/// migrations can be scaffolded without booting the web host.
/// </summary>
public class WastaDbContextFactory : IDesignTimeDbContextFactory<WastaDbContext>
{
    public WastaDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Wasta")
            ?? "Host=localhost;Port=55432;Database=wasta;Username=postgres;Password=wasta_local_dev";

        var options = new DbContextOptionsBuilder<WastaDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable(WastaDbContext.MigrationsHistoryTable))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new WastaDbContext(options);
    }
}
