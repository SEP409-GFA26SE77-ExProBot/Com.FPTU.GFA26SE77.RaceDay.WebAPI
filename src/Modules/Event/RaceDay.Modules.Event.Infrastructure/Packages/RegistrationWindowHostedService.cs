using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Packages;

/// <summary>
/// Định kỳ mở/đóng ĐK theo <see cref="RaceEvent.RegistrationOpensAtUtc"/> /
/// <see cref="RaceEvent.RegistrationClosesAtUtc"/>.
/// </summary>
public sealed class RegistrationWindowHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<RegistrationWindowHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

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
                logger.LogError(ex, "Registration window sweep failed.");
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

        var toOpen = await db.RaceEvents
            .Where(e => e.Status == RaceEventStatus.Approved
                        && e.RegistrationOpensAtUtc != null
                        && e.RegistrationOpensAtUtc <= now)
            .ToListAsync(ct);

        var toClose = await db.RaceEvents
            .Where(e => e.Status == RaceEventStatus.RegistrationOpen
                        && e.RegistrationClosesAtUtc != null
                        && e.RegistrationClosesAtUtc <= now)
            .ToListAsync(ct);

        var opened = 0;
        var closed = 0;
        foreach (var race in toOpen)
        {
            if (race.TryAutoOpenRegistration(now))
                opened++;
        }

        foreach (var race in toClose)
        {
            if (race.TryAutoCloseRegistration(now))
                closed++;
        }

        if (opened + closed > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Registration window: opened {Opened}, closed {Closed}.",
                opened,
                closed);
        }
    }
}
