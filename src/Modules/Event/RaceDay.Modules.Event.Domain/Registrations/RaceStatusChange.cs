using RaceDay.Shared.Domain.Abstractions;

namespace RaceDay.Modules.Event.Domain.Registrations;

/// <summary>Audit record for a manual DNS/DNF/DQ decision.</summary>
public sealed class RaceStatusChange : Entity
{
    public Guid RegistrationId { get; private set; }
    public RaceStatus PreviousStatus { get; private set; }
    public RaceStatus Status { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid ChangedByUserId { get; private set; }
    public DateTime ChangedAtUtc { get; private set; }

    private RaceStatusChange() { }

    internal static RaceStatusChange Create(Guid registrationId, RaceStatus previousStatus,
        RaceStatus status, string reason, Guid changedByUserId, DateTime changedAtUtc) => new()
    {
        RegistrationId = registrationId,
        PreviousStatus = previousStatus,
        Status = status,
        Reason = reason,
        ChangedByUserId = changedByUserId,
        ChangedAtUtc = changedAtUtc
    };
}
