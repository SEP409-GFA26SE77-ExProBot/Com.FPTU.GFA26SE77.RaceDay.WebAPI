using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.RaceMonitoring.Contracts.Timeline;
using RaceDay.Modules.Simulation.Application;

namespace RaceDay.Modules.Simulation.Api;

/// <summary>
/// Writes the simulator's checkpoint crossings to the race timeline in arrival order, outside the clock tick.
/// A failed batch is logged and skipped; the simulation itself is never affected.
/// </summary>
public sealed class SimulationTimelineWorker(
    SimulationTimelineFeed feed,
    IServiceScopeFactory scopes,
    ILogger<SimulationTimelineWorker> logger) : BackgroundService
{
    private const int MaxBatch = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = feed.Reader;
        try
        {
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                var pending = new List<SimulatedCheckpointPassage>();
                while (reader.TryRead(out var work))
                {
                    switch (work)
                    {
                        case SimulationTimelinePassages passages:
                            pending.AddRange(passages.Passages);
                            break;
                        case SimulationTimelineRunStarted started:
                            await FlushAsync(pending, stoppingToken);
                            await RunAsync(
                                sink => sink.ClearOtherRunsAsync(started.EventId, started.DistanceId, started.RunId, stoppingToken),
                                stoppingToken);
                            break;
                    }

                    if (pending.Count >= MaxBatch)
                    {
                        await FlushAsync(pending, stoppingToken);
                    }
                }

                await FlushAsync(pending, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task FlushAsync(List<SimulatedCheckpointPassage> pending, CancellationToken cancellationToken)
    {
        if (pending.Count == 0)
        {
            return;
        }

        var batch = pending.ToList();
        pending.Clear();
        await RunAsync(sink => sink.RecordAsync(batch, cancellationToken), cancellationToken);
    }

    private async Task RunAsync(Func<ISimulationTimelineSink, Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await action(scope.ServiceProvider.GetRequiredService<ISimulationTimelineSink>());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Không ghi được timeline giả lập.");
        }
    }
}
