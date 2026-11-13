using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Packages;

/// <summary>
/// Hết TTL giữ chỗ PendingPayment → nhả RegisteredCount + Cancelled.
/// </summary>
public sealed class RegistrationSlotHoldExpiryHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<RegistrationSlotHoldExpiryHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Registration slot-hold expiry sweep failed.");
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

    private async Task SweepAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EventDbContext>();
        var now = DateTime.UtcNow;

        var expired = await db.Registrations
            .Where(r => r.Status == RegistrationStatus.PendingPayment
                        && r.SlotHeldUntilUtc != null
                        && r.SlotHeldUntilUtc <= now)
            .OrderBy(r => r.SlotHeldUntilUtc)
            .Take(100)
            .ToListAsync(ct);

        if (expired.Count == 0)
        {
            return;
        }

        var distanceIds = expired.Select(r => r.DistanceId).Distinct().ToList();
        var distances = await db.Distances
            .Where(d => distanceIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, ct);

        var released = 0;
        foreach (var reg in expired)
        {
            if (distances.TryGetValue(reg.DistanceId, out var distance))
            {
                distance.DecrementRegistered();
            }
            else
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE event.event_distances
                    SET "RegisteredCount" = GREATEST("RegisteredCount" - 1, 0)
                    WHERE "Id" = {reg.DistanceId}
                    """,
                    ct);
            }

            reg.CancelUnpaid();
            released++;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Registration slot-hold expiry: released {Count} holds.", released);
    }
}
