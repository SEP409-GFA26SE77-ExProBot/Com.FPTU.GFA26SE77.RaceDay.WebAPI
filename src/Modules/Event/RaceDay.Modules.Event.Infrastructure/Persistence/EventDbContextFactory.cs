using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RaceDay.Modules.Event.Infrastructure.Persistence;

/// <summary>Generate migrations without starting API hosted services or seeders.</summary>
public sealed class EventDbContextFactory : IDesignTimeDbContextFactory<EventDbContext>
{
    public EventDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5433;Database=raceday;Username=postgres";
        return new EventDbContext(new DbContextOptionsBuilder<EventDbContext>()
            .UseNpgsql(connection, pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", "event")).Options);
    }
}
