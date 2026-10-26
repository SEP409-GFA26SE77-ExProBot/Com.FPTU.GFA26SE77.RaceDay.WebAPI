using RaceDay.Shared.Domain.Abstractions;

namespace RaceDay.Modules.Event.Domain.Registrations;

public enum RunnerRaceStatus { NotStarted, Started, Finished, Dns, Dnf, Dq }
public enum PhysicalBibStatus { Normal, Lost, Reissued }

/// <summary>Immutable receipt and audit entry, saved atomically with the registration.</summary>
public sealed class RunnerOperation : Entity
{
    public Guid RegistrationId { get; private set; }
    public Guid RequestId { get; private set; }
    public string Kind { get; private set; } = string.Empty;
    public string RequestedStatus { get; private set; } = string.Empty;
    public string PreviousStatus { get; private set; } = string.Empty;
    public string NewStatus { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public Guid ActorUserId { get; private set; }
    public long ExpectedVersion { get; private set; }
    public long Version { get; private set; }
    public string BibNumber { get; private set; } = string.Empty;
    public RunnerRaceStatus RaceStatus { get; private set; }
    public PhysicalBibStatus BibStatus { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private RunnerOperation() { }

    public static RunnerOperation Create(EventRegistration registration, Guid requestId,
        string kind, string requestedStatus, string previousStatus, string newStatus,
        string reason, Guid actorUserId, long expectedVersion) => new()
        {
            RegistrationId = registration.Id, RequestId = requestId, Kind = kind,
            RequestedStatus = requestedStatus, PreviousStatus = previousStatus, NewStatus = newStatus,
            Reason = reason, ActorUserId = actorUserId, ExpectedVersion = expectedVersion,
            Version = registration.Version, BibNumber = registration.BibNumber!,
            RaceStatus = registration.RaceStatus, BibStatus = registration.BibStatus,
            CreatedAtUtc = DateTime.UtcNow
        };
}
