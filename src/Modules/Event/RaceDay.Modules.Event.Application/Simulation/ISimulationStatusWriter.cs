using RaceDay.Modules.Event.Contracts.Simulation;

namespace RaceDay.Modules.Event.Application.Simulation;

public interface ISimulationStatusWriter
{
    Task<SimulationCommitItemResult> WriteAsync(Guid eventId, Guid distanceId, SimulationFinishDraft draft,
        CancellationToken cancellationToken = default);
}
