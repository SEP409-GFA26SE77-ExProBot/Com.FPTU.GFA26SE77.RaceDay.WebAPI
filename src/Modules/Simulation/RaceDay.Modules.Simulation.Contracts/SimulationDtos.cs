namespace RaceDay.Modules.Simulation.Contracts;

public sealed record SimulationPointDto(double Lat, double Lng);

public sealed record SimulationCheckpointDto(
    string Id,
    string Name,
    double Km,
    double Lat,
    double Lng,
    string Type);

public sealed record SimulationPreviewDto(
    Guid EventId,
    Guid DistanceId,
    string Title,
    double DistanceKm,
    int EligibleCount,
    bool CourseReady,
    string? CourseError,
    IReadOnlyList<SimulationPointDto> Polyline,
    IReadOnlyList<SimulationCheckpointDto> Checkpoints,
    Guid? ActiveRunId);

public sealed record SimulationSplitDto(
    string CheckpointId,
    string Name,
    double Km,
    double SimSeconds);

public sealed record SimulationAthleteDto(
    Guid RegistrationId,
    string Bib,
    string FullName,
    string ChipId,
    double BaseKmh,
    double SpeedKmh,
    double CourseKm,
    string Motion,
    bool Sos,
    bool ValidFinish,
    long? ResultSeconds,
    double? FinishSimSeconds,
    IReadOnlyList<SimulationSplitDto> Splits);

public sealed record SimulationRankDto(
    int Rank,
    Guid RegistrationId,
    string Bib,
    string FullName,
    long ResultSeconds,
    double FinishSimSeconds);

public sealed record SimulationCheckpointLeaderDto(
    string CheckpointId,
    Guid RegistrationId,
    string Bib,
    double SimSeconds);

public sealed record SimulationLogDto(
    long Sequence,
    double SimSeconds,
    string Kind,
    string Subject);

/// <summary>
/// Full state of one in-memory demo run. <see cref="Version"/> increases by 1 on each change.
/// <see cref="Generation"/> increases when Reset or New Run replaces the run; <see cref="Version"/> then restarts at 1.
/// </summary>
public sealed record SimulationSnapshotDto(
    Guid RunId,
    int Generation,
    long Version,
    Guid EventId,
    Guid DistanceId,
    string Phase,
    string CommitState,
    int Seed,
    double PlaybackRate,
    double SimulatedSeconds,
    double DistanceKm,
    string Title,
    bool DiscardRequired,
    IReadOnlyList<Guid> CommittedRegistrationIds,
    IReadOnlyList<SimulationPointDto> Polyline,
    IReadOnlyList<SimulationCheckpointDto> Checkpoints,
    IReadOnlyList<SimulationAthleteDto> Athletes,
    IReadOnlyList<SimulationRankDto> Ranking,
    IReadOnlyList<SimulationCheckpointLeaderDto> CheckpointLeaders,
    IReadOnlyList<SimulationLogDto> Log,
    IReadOnlyList<SimulationDecisionDto>? Decisions = null);

public sealed record SimulationDecisionDto(
    Guid Id, Guid RegistrationId, string Bib, string PreviousStatus, string Status,
    string Reason, string? Evidence, Guid ActorId, string CheckpointId, DateTime CreatedAtUtc,
    string Resolution, Guid? ReviewedBy = null, DateTime? ReviewedAtUtc = null, string? ReviewReason = null,
    double? CreatedAtSimSeconds = null, double? ReviewedAtSimSeconds = null);

public sealed record StaffCheckpointAthleteDto(
    Guid RegistrationId, string Bib, string FullName, string Motion, bool Sos,
    double CourseKm, double SpeedKmh, double? PassedAtSeconds, double RemainingKm,
    bool CanDnf, bool CanDns, bool CanReport);

public sealed record StaffCheckpointSnapshotDto(
    Guid EventId, string EventName, Guid DistanceId, string Title,
    SimulationCheckpointDto Checkpoint, IReadOnlyList<SimulationPointDto> Polyline, double DistanceKm,
    Guid? RunId, int Generation, long Version, string? Phase, double SimulatedSeconds,
    double PlaybackRate, string? CommitState,
    IReadOnlyList<StaffCheckpointAthleteDto> Athletes, IReadOnlyList<SimulationDecisionDto> Decisions);

public sealed record SimulationCommitItemDto(
    Guid RegistrationId,
    string Outcome,
    string? Reason,
    long? ResultSeconds);

public sealed record SimulationCommitResponseDto(
    bool Accepted,
    string? RefusalReason,
    IReadOnlyList<SimulationCommitItemDto> Items,
    SimulationSnapshotDto Snapshot);

/// <summary>Sent on the previous hub group when Reset or New Run replaces the run.</summary>
public sealed record SimulationRunReplacedDto(
    Guid PreviousRunId,
    int PreviousGeneration,
    Guid RunId,
    int Generation,
    long Version);
