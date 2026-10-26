using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Domain.Abstractions;

namespace RaceDay.Modules.RaceMonitoring.Domain.AidStations;

/// <summary>
/// Aid station for an event. EventId refers to event.race_events and CheckpointId refers to
/// event.event_checkpoints. Neither identifier has a cross-schema foreign key.
/// </summary>
public sealed class AidStation : AggregateRoot
{
    public Guid EventId { get; private set; }
    public Guid? CheckpointId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public AidStationStatus Status { get; private set; }

    private AidStation()
    {
    }

    public static AidStation Create(
        Guid eventId,
        Guid? checkpointId,
        string name,
        double latitude,
        double longitude,
        AidStationStatus status)
    {
        CoordinatorInvariants.RequiredId(eventId, "Tham chiếu sự kiện không hợp lệ.");
        CoordinatorInvariants.OptionalId(checkpointId, "Tham chiếu checkpoint không hợp lệ.");
        RunnerLocation.EnsureCoordinates(latitude, longitude);

        return new AidStation
        {
            EventId = eventId,
            CheckpointId = checkpointId,
            Name = CoordinatorInvariants.RequiredText(
                name,
                CoordinatorFieldLengths.Name,
                "Tên trạm tiếp tế không được để trống.",
                "Tên trạm tiếp tế tối đa 200 ký tự."),
            Latitude = latitude,
            Longitude = longitude,
            Status = CoordinatorInvariants.RequiredEnum(status, "Trạng thái trạm tiếp tế không hợp lệ.")
        };
    }
}
