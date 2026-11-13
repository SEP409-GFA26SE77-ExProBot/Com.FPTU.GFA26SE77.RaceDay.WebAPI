namespace RaceDay.Modules.Event.Contracts.Simulation;

/// <summary>Server flag. When disabled the commit port writes nothing.</summary>
public interface ISimulationDemoGate
{
    bool Enabled { get; }

    /// <summary>OS time-zone id. Empty refuses the demo finish write.</summary>
    string? StartTimeZone { get; }
}

public sealed record SimulationRegistrationState(
    Guid RegistrationId,
    Guid EventId,
    Guid DistanceId,
    string Bib,
    string FullName,
    string Status,
    string RaceStatus,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc,
    long? ResultSeconds);

public interface ISimulationCandidateQuery
{
    Task<IReadOnlyList<SimulationRegistrationState>> ListEligibleAsync(
        Guid eventId,
        Guid distanceId,
        CancellationToken cancellationToken = default);

    /// <summary>Unfiltered read used to classify a commit. Null when the registration does not exist.</summary>
    Task<SimulationRegistrationState?> GetAsync(
        Guid registrationId,
        CancellationToken cancellationToken = default);
}

public sealed record OfficialStartClocks(
    DateOnly EventDate,
    TimeOnly? DistanceStartAt,
    TimeOnly? EventStartTime);

public interface ISimulationOfficialStartSource
{
    Task<OfficialStartClocks?> GetClocksAsync(
        Guid eventId,
        Guid distanceId,
        CancellationToken cancellationToken = default);
}

public static class SimulationCommitOutcomes
{
    public const string CommittedThisRun = "CommittedThisRun";
    public const string AlreadyRecordedElsewhere = "AlreadyRecordedElsewhere";
    public const string Skipped = "Skipped";
    public const string Retryable = "Retryable";
    public const string Unknown = "Unknown";
}

/// <summary>Checkpoint external id from the map course; elapsed from official start.</summary>
public sealed record SimulationSplitDraft(string CheckpointExternalId, double ElapsedSeconds);

/// <summary>Bib is the value captured on the simulation run, not a later registration bib.</summary>
public sealed record SimulationFinishDraft(
    Guid RegistrationId,
    double ElapsedSeconds,
    string Bib,
    IReadOnlyList<SimulationSplitDraft>? Splits = null,
    SimulationStatusDraft? Decision = null);

public sealed record SimulationStatusDraft(
    Guid RunId, string Status, string Reason, Guid ActorId, string CheckpointId, DateTime ChangedAtUtc);

/// <summary>Actor id is taken from the server user, never from this command.</summary>
public sealed record SimulationCommitCommand(
    Guid EventId,
    Guid DistanceId,
    IReadOnlyList<SimulationFinishDraft> Drafts,
    IReadOnlyCollection<Guid> AlreadyCommittedByThisRun,
    IReadOnlyCollection<Guid>? UnresolvedRegistrationIds = null);

public sealed record SimulationCommitItemResult(
    Guid RegistrationId,
    string Outcome,
    string? Reason,
    long? ResultSeconds);

public sealed record SimulationCommitBatch(
    bool Accepted,
    string? RefusalReason,
    IReadOnlyList<SimulationCommitItemResult> Items);

public interface ISimulationResultCommitPort
{
    Task<SimulationCommitBatch> CommitAsync(
        SimulationCommitCommand command,
        CancellationToken cancellationToken = default);
}
