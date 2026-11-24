using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaceDay.Modules.RaceMonitoring.Application.CutoffWarnings;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.CutoffWarnings;

public sealed class CutoffMonitoringOptions
{
    public const string SectionName = "RaceMonitoring:CutoffMonitoring";

    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 30;
    /// <summary>Delay after a cut-off before warning, so late RFID uploads are not reported as missing runners.</summary>
    public int GracePeriodSeconds { get; set; } = 120;
    public int LookBackHours { get; set; } = 12;
    public bool RequireStartEvidence { get; set; } = true;
    public string TimeZoneId { get; set; } = CutoffClock.DefaultTimeZoneId;
}

/// <summary>Periodic server-side cut-off check. Each tick runs in its own scope.</summary>
public sealed class CutoffWarningMonitor(
    IServiceScopeFactory scopes,
    IOptions<CutoffMonitoringOptions> options,
    TimeProvider clock,
    ILogger<CutoffWarningMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = options.Value;
        if (!config.Enabled)
        {
            logger.LogInformation("Cut-off monitoring is disabled.");
            return;
        }

        var settings = new CutoffMonitoringSettings(
            TimeSpan.FromSeconds(Math.Max(0, config.GracePeriodSeconds)),
            TimeSpan.FromHours(Math.Max(1, config.LookBackHours)),
            config.RequireStartEvidence,
            CutoffClock.ResolveZone(config.TimeZoneId));
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, config.IntervalSeconds)), clock);
        // First tick after one interval: the seeder migrates the schema concurrently at startup.
        while (await WaitAsync(timer, stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                await sender.Send(
                    new DetectCutoffWarningsCommand(clock.GetUtcNow().UtcDateTime, settings),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Cut-off monitor tick failed.");
            }
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
