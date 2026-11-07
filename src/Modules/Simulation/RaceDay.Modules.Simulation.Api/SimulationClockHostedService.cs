using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Simulation.Application;

namespace RaceDay.Modules.Simulation.Api;

public sealed class SimulationClockHostedService(
    SimulationSessionBook book,
    ILogger<SimulationClockHostedService> logger) : BackgroundService
{
    public static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(250);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Tick);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await book.AdvanceRunningAsync(Tick.TotalSeconds, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Simulation clock tick failed.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
