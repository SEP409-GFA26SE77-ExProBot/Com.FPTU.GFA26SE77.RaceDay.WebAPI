namespace RaceDay.Modules.Event.Contracts.Simulation;

public sealed record CheckpointResultRow(
    Guid RegistrationId, string Bib, string FullName, string Motion, double? PassedAtSeconds);

public sealed record CheckpointResultDecision(
    Guid RegistrationId, string Bib, string Status, string Reason, string? Evidence, string Resolution);

public sealed record CheckpointResultExport(
    Guid RunId, Guid EventId, Guid DistanceId, string CheckpointId, string CheckpointName,
    double CheckpointKm, Guid StaffUserId, DateTime SubmittedAtUtc,
    IReadOnlyList<CheckpointResultRow> Rows, IReadOnlyList<CheckpointResultDecision> Decisions);

public interface ICheckpointResultExportStore
{
    Task<CheckpointResultExport> SubmitAsync(CheckpointResultExport export, CancellationToken cancellationToken = default);
    Task<CheckpointResultExport?> GetAsync(Guid runId, string checkpointId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CheckpointResultExport>> ListAsync(Guid eventId, Guid distanceId, CancellationToken cancellationToken = default);
}
