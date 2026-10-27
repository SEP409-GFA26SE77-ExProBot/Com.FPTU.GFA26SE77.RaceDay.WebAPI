using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Device.Application.Abstractions;
using RaceDay.Modules.Device.Contracts.Sos;
using RaceDay.Modules.Device.Infrastructure.Auth;
using RaceDay.Modules.Device.Infrastructure.Persistence;
using RaceDay.Modules.Device.Infrastructure.Repositories;
using RaceDay.Modules.Device.Infrastructure.Sos;

namespace RaceDay.Modules.Device.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDeviceInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<DeviceDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "device")));

        services.AddScoped<IDeviceAuthRequestRepository, DeviceAuthRequestRepository>();
        services.AddScoped<ILinkedDeviceRepository, LinkedDeviceRepository>();
        services.AddScoped<ISosAlertRepository, SosAlertRepository>();
        services.AddScoped<ISosAlertScopeQuery, SosAlertScopeQuery>();
        services.AddScoped<IDeviceUnitOfWork, DeviceUnitOfWork>();
        services.AddSingleton<IWearTokenService, WearTokenService>();
        services.AddSingleton<IDeviceFrontendOptions, DeviceFrontendOptions>();
        services.AddHostedService<DeviceDataSeeder>();
        return services;
    }
}

public sealed class DeviceFrontendOptions(IConfiguration configuration) : IDeviceFrontendOptions
{
    public string VerificationBaseUrl
    {
        get
        {
            var baseUrl = (configuration["Frontend:UserAppUrl"] ?? "http://localhost:3000").TrimEnd('/');
            if (baseUrl.EndsWith("/login", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = baseUrl[..^"/login".Length];
            }

            return string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:3000" : baseUrl;
        }
    }
}

public sealed class DeviceDataSeeder(
    IServiceScopeFactory scopeFactory,
    ILogger<DeviceDataSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DeviceDbContext>();
        try
        {
            await db.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Device schema migrated.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Device database migrate failed.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
