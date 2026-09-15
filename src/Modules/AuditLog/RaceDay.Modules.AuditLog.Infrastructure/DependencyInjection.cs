using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Modules.AuditLog.Application.Abstractions;
using RaceDay.Modules.AuditLog.Infrastructure.Repositories;

namespace RaceDay.Modules.AuditLog.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAuditLogInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        _ = configuration;
        services.AddScoped<IAuditEntryRepository, AuditEntryRepository>();
        return services;
    }
}
