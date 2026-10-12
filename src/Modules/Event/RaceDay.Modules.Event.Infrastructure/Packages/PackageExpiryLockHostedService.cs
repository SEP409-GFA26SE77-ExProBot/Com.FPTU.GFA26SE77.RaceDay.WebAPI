using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Packages;

/// <summary>
/// Định kỳ khóa giải khi <see cref="RaceEvent.PackageExpiresAtUtc"/> đã qua.
/// </summary>
public sealed class PackageExpiryLockHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<PackageExpiryLockHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await LockExpiredAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Package expiry lock sweep failed.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task LockExpiredAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EventDbContext>();
        var now = DateTime.UtcNow;

        var candidates = await db.RaceEvents
            .Where(e => e.PackageExpiresAtUtc != null
                        && e.PackageExpiresAtUtc <= now
                        && e.Status != RaceEventStatus.Suspended
                        && e.Status != RaceEventStatus.Cancelled
                        && e.Status != RaceEventStatus.Completed)
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return;

        var locked = 0;
        foreach (var race in candidates)
        {
            if (race.TrySuspendForExpiredPackage(now))
                locked++;
        }

        if (locked > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Suspended {Count} events due to expired package.", locked);
        }
    }
}
