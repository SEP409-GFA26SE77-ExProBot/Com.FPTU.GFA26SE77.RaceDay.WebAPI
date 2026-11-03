using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Application.CheckpointScans;
using RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence;
using RaceDay.Modules.RaceMonitoring.Infrastructure.RaceEventLogs;

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
        services.AddScoped<ICheckpointScanRepository, CheckpointScanRepository>();
        services.AddScoped<IRaceEventLogRepository, RaceEventLogRepository>();
        services.AddScoped<ICheckpointScanRecorder, CheckpointScanRecorder>();
        services.AddSingleton<InMemoryRaceEventLogBroker>();
        services.AddSingleton<IRaceEventLogBroker>(sp => sp.GetRequiredService<InMemoryRaceEventLogBroker>());
        services.AddSingleton<IRaceEventLogPublisher>(sp => sp.GetRequiredService<InMemoryRaceEventLogBroker>());
        services.AddScoped<IRaceMonitoringUnitOfWork, RaceMonitoringUnitOfWork>();
        services.AddHostedService<RaceMonitoringDataSeeder>();
        return services;
    }
}
