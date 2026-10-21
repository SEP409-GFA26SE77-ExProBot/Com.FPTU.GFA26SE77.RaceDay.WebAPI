using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Photo.Application.Abstractions;
using RaceDay.Modules.Photo.Infrastructure.Ocr;
using RaceDay.Modules.Photo.Infrastructure.Persistence;
using RaceDay.Modules.Photo.Infrastructure.Repositories;

namespace RaceDay.Modules.Photo.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPhotoInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<PhotoDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "photo")));

        services.AddScoped<IPhotoAssetRepository, PhotoAssetRepository>();
        services.AddScoped<IPhotoDisputeRepository, PhotoDisputeRepository>();
        services.AddScoped<IPhotoUnitOfWork, PhotoUnitOfWork>();

        var ocrBase = configuration["Photo:Ocr:BaseUrl"];
        services.AddHttpClient<IOcrClient, OcrClient>(client =>
        {
            if (!string.IsNullOrWhiteSpace(ocrBase))
            {
                client.BaseAddress = new Uri(ocrBase.TrimEnd('/') + "/");
            }

            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHostedService<PhotoDataSeeder>();
        services.AddHostedService<PhotoOcrWorker>();
        return services;
    }
}

public sealed class PhotoDataSeeder(
    IServiceScopeFactory scopeFactory,
    ILogger<PhotoDataSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PhotoDbContext>();
        try
        {
            await db.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Photo schema migrated.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Photo database migrate failed.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
