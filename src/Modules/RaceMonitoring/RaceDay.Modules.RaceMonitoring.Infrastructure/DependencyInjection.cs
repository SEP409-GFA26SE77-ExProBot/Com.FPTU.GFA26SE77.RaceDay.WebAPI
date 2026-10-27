using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
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

        services.AddScoped<IEmergencyResponseRepository, EmergencyResponseRepository>();
        services.AddScoped<IEmergencyResourceRepository, EmergencyResourceRepository>();
        services.AddScoped<IEmergencyAssignmentRepository, EmergencyAssignmentRepository>();
        services.AddScoped<IRaceMonitoringUnitOfWork, RaceMonitoringUnitOfWork>();
        services.AddHostedService<RaceMonitoringDataSeeder>();
        return services;
    }
}
