using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Application.CheckpointScans;
using RaceDay.Modules.RaceMonitoring.Application.CutoffWarnings;
using RaceDay.Modules.RaceMonitoring.Application.RaceEventLogs;
using RaceDay.Modules.RaceMonitoring.Contracts.StaffIncidents;
using RaceDay.Modules.RaceMonitoring.Contracts.Timeline;
using RaceDay.Modules.RaceMonitoring.Infrastructure.CutoffWarnings;
using RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence;
using RaceDay.Modules.RaceMonitoring.Infrastructure.RaceEventLogs;
using RaceDay.Modules.RaceMonitoring.Infrastructure.StaffIncidents;

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
        services.AddScoped<IRfidTagAssignmentRepository, RfidTagAssignmentRepository>();
        services.AddScoped<ICheckpointScanRecorder, CheckpointScanRecorder>();
        services.AddScoped<RaceEventLogChipStarts>();
        services.AddSingleton<SimulationRunAnchorCache>();
        services.AddScoped<ISimulationTimelineSink, SimulationTimelineSink>();
        services.AddScoped<ICheckpointActivityQuery, CheckpointActivityQuery>();
        services.AddScoped<StaffIncidentStore>();
        services.AddScoped<IStaffIncidentSink>(sp => sp.GetRequiredService<StaffIncidentStore>());
        services.AddScoped<IStaffIncidentQuery>(sp => sp.GetRequiredService<StaffIncidentStore>());
        services.AddSingleton<InMemoryRaceEventLogBroker>();
        services.AddSingleton<IRaceEventLogBroker>(sp => sp.GetRequiredService<InMemoryRaceEventLogBroker>());
        services.AddSingleton<IRaceEventLogPublisher>(sp => sp.GetRequiredService<InMemoryRaceEventLogBroker>());
        services.AddScoped<IRaceMonitoringUnitOfWork, RaceMonitoringUnitOfWork>();
        services.AddHostedService<RaceMonitoringDataSeeder>();

        services.TryAddSingleton(TimeProvider.System);
        var cutoffMonitoring = new CutoffMonitoringOptions();
        configuration.GetSection(CutoffMonitoringOptions.SectionName).Bind(cutoffMonitoring);
        services.AddSingleton(Options.Create(cutoffMonitoring));
        services.AddScoped<ICutoffWarningRepository, CutoffWarningRepository>();
        services.AddScoped<ICutoffWarningPassageResolver, CutoffWarningPassageResolver>();
        services.AddScoped<CutoffWarningAccess>();
        services.AddSingleton<InMemoryCutoffWarningBroker>();
        services.AddSingleton<ICutoffWarningBroker>(sp => sp.GetRequiredService<InMemoryCutoffWarningBroker>());
        services.AddSingleton<ICutoffWarningPublisher>(sp => sp.GetRequiredService<InMemoryCutoffWarningBroker>());
        services.AddHostedService<CutoffWarningMonitor>();
        return services;
    }
}
