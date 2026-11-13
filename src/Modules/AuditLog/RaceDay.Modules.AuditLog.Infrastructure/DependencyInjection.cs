using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.AuditLog.Application.Abstractions;
using RaceDay.Modules.AuditLog.Infrastructure.Persistence;
using RaceDay.Modules.AuditLog.Infrastructure.Repositories;

namespace RaceDay.Modules.AuditLog.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAuditLogInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AuditDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "audit")));

        services.AddScoped<IAuditEntryRepository, AuditEntryRepository>();
        services.AddHostedService<AuditSchemaMigrator>();
        return services;
    }
}

public sealed class AuditSchemaMigrator(
    IServiceScopeFactory scopeFactory,
    ILogger<AuditSchemaMigrator> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        try
        {
            await db.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Audit schema migrated (Postgres).");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Audit database migrate failed.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
