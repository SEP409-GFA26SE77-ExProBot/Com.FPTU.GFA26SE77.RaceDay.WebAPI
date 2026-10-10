using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRaceMonitoringInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        _ = configuration;
        return services;
    }
}
