using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RaceDay.Modules.Photo.Infrastructure.Persistence;

public sealed class PhotoDbContextFactory : IDesignTimeDbContextFactory<PhotoDbContext>
{
    public PhotoDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PhotoDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=raceday;Username=postgres;Password=postgres",
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "photo"))
            .Options;
        return new PhotoDbContext(options);
    }
}
