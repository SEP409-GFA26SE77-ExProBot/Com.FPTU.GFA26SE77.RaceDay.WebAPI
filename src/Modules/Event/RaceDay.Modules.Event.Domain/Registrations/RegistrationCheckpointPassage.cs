using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Domain.Registrations;

public sealed class RegistrationCheckpointPassage : Entity
{
    public Guid RegistrationId { get; private set; }
    public Guid EventId { get; private set; }
    public Guid DistanceId { get; private set; }
    public Guid CheckpointId { get; private set; }
    public DateTime PassedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private RegistrationCheckpointPassage()
    {
    }

    public static RegistrationCheckpointPassage Create(
        Guid registrationId,
        Guid eventId,
        Guid distanceId,
        Guid checkpointId,
        DateTime passedAtUtc)
    {
        if (registrationId == Guid.Empty || eventId == Guid.Empty || distanceId == Guid.Empty || checkpointId == Guid.Empty)
        {
            throw new DomainException("Thông tin checkpoint passage không hợp lệ.");
        }

        if (passedAtUtc.Kind != DateTimeKind.Utc)
        {
            passedAtUtc = DateTime.SpecifyKind(passedAtUtc, DateTimeKind.Utc);
        }

        return new RegistrationCheckpointPassage
        {
            Id = Guid.NewGuid(), // <-- QUAN TRỌNG: Gán ID mới để không bị Guid.Empty
            RegistrationId = registrationId,
            EventId = eventId,
            DistanceId = distanceId,
            CheckpointId = checkpointId,
            PassedAtUtc = passedAtUtc,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}