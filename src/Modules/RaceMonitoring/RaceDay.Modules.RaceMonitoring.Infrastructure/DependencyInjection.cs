using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRaceMonitoringInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<RaceMonitoringDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "race_monitoring")));

        services.AddHostedService<RaceMonitoringDataSeeder>();
        return services;
    }
}
