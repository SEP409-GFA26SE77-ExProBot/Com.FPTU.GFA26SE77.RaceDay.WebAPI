using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Finance.Application.Abstractions;
using RaceDay.Modules.Finance.Domain.Settings;
using RaceDay.Modules.Finance.Infrastructure.Persistence;
using RaceDay.Modules.Finance.Infrastructure.Repositories;

namespace RaceDay.Modules.Finance.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddFinanceInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<FinanceDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "finance")));

        services.AddScoped<IFinanceUnitOfWork, FinanceUnitOfWork>();
        services.AddScoped<IEventLedgerRepository, EventLedgerRepository>();
        services.AddScoped<IPayoutRequestRepository, PayoutRequestRepository>();
        services.AddScoped<IRefundRequestRepository, RefundRequestRepository>();
        services.AddScoped<IOrganizerBankAccountRepository, OrganizerBankAccountRepository>();
        services.AddScoped<IPlatformFinanceSettingsRepository, PlatformFinanceSettingsRepository>();
        services.AddHostedService<FinanceDataSeeder>();
        return services;
    }
}

public sealed class FinanceDataSeeder(
    IServiceScopeFactory scopeFactory,
    ILogger<FinanceDataSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        try
        {
            await db.Database.MigrateAsync(cancellationToken);
            if (!await db.PlatformSettings.AnyAsync(x => x.Id == PlatformFinanceSettings.SingletonId, cancellationToken))
            {
                db.PlatformSettings.Add(PlatformFinanceSettings.CreateDefault());
                await db.SaveChangesAsync(cancellationToken);
            }

            logger.LogInformation("Finance schema migrated.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Finance database migrate failed.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
