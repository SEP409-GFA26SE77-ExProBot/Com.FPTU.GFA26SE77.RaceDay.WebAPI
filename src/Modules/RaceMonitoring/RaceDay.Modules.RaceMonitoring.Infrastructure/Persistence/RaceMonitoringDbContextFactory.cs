using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence;

/// <summary>
/// Design-time only. Uses the local Docker Postgres from .env.example so EF tooling
/// does not read .env or a remote connection string. migrations add does not connect.
/// </summary>
public sealed class RaceMonitoringDbContextFactory : IDesignTimeDbContextFactory<RaceMonitoringDbContext>
{
    public RaceMonitoringDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RaceMonitoringDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=postgres",
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "race_monitoring"))
            .Options;
        return new RaceMonitoringDbContext(options);
    }
}
