using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Map.Application.Courses;
using RaceDay.Modules.Map.Application.Geocode;
using RaceDay.Modules.Map.Infrastructure.Geocode;
using RaceDay.Modules.Map.Infrastructure.Persistence;
using RaceDay.Modules.Map.Infrastructure.Repositories;

namespace RaceDay.Modules.Map.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMapInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MapDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "map")));

        services.AddScoped<IRaceCourseRepository, RaceCourseRepository>();
        services.AddScoped<RaceDay.Modules.Map.Contracts.Courses.IRaceCourseReadQuery,
            RaceDay.Modules.Map.Infrastructure.Courses.RaceCourseReadQuery>();
        services.AddScoped<RaceDay.Modules.Map.Contracts.Courses.IEventCoursesReadQuery,
            RaceDay.Modules.Map.Infrastructure.Courses.RaceCourseReadQuery>();
        services.AddScoped<IMapUnitOfWork, MapUnitOfWork>();
        // Nominatim OSM DNS hay fail ở VN; Photon (komoot) ổn định hơn, không cần API key.
        services.AddHttpClient(PhotonGeocodeService.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://photon.komoot.io/");
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "RaceDay-Api/1.0 (organizer-setup-map; contact=support@raceday.vn)");
            client.Timeout = TimeSpan.FromSeconds(12);
        });
        services.AddScoped<IGeocodeService, PhotonGeocodeService>();
        services.AddHostedService<MapDataSeeder>();
        return services;
    }
}

public sealed class MapDataSeeder(
    IServiceScopeFactory scopeFactory,
    ILogger<MapDataSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MapDbContext>();
        try
        {
            await db.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Map schema migrated.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Map database migrate failed.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
