namespace RaceDay.Modules.Event.Contracts.Simulation;

public sealed record SimulationJournalEntry(
    Guid Id, long Sequence, Guid RunId, Guid EventId, Guid DistanceId, string CheckpointId,
    string Kind, Guid ActorId, string ActorName, string ActorRole, Guid? RegistrationId,
    string? Bib, string? FullName, DateTime OccurredAtUtc, double? SimSeconds,
    string? PreviousStatus, string? Status, string? Reason, string? Evidence, Guid? DecisionId,
    Guid OperationId, string RequestHash);

public sealed record SimulationJournalRun(Guid RunId, DateTime LastOccurredAtUtc);
public sealed record SimulationJournalPage(IReadOnlyList<SimulationJournalEntry> Items, long? NextCursor);
public sealed record SimulationJournalQuery(Guid EventId, Guid DistanceId, string CheckpointId,
    Guid RunId, string? Kind = null, string? Search = null, Guid? ActorId = null, long? Cursor = null);

/// <summary>Durable checkpoint business history. Implemented by Event; simulation state stays in memory.</summary>
public interface ISimulationJournalStore
{
    Task<SimulationJournalEntry?> FindOperationAsync(Guid operationId, CancellationToken ct = default);
    Task<SimulationJournalEntry> AppendAsync(SimulationJournalEntry entry, CancellationToken ct = default);
    Task<SimulationJournalPage> ReadAsync(SimulationJournalQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<SimulationJournalRun>> RunsAsync(Guid eventId, Guid distanceId, string checkpointId, CancellationToken ct = default);
    Task<long> VersionAsync(Guid eventId, Guid distanceId, string checkpointId, CancellationToken ct = default);
    Task<CheckpointResultExport?> GetExportAsync(Guid runId, string checkpointId, CancellationToken ct = default);
    Task<CheckpointResultExport> SubmitExportAsync(CheckpointResultExport export, SimulationJournalEntry entry, CancellationToken ct = default);
}
