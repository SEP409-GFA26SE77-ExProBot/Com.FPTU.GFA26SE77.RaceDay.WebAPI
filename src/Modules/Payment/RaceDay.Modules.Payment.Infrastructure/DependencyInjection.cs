using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Payment.Application;
using RaceDay.Modules.Payment.Infrastructure.PayOs;
using RaceDay.Modules.Payment.Infrastructure.Persistence;

namespace RaceDay.Modules.Payment.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<PaymentDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "payment")));

        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentUnitOfWork, PaymentUnitOfWork>();
        services.AddHttpClient<IPayOsGateway, PayOsGateway>();
        services.AddHostedService<PaymentDataSeeder>();
        return services;
    }
}

public sealed class PaymentDataSeeder(
    IServiceScopeFactory scopeFactory,
    ILogger<PaymentDataSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        try
        {
            await db.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Payment schema migrated.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Payment database migrate failed.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
