using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Device.Domain.Sos;

public enum SosStatus
{
    Open = 0,
    Acknowledged = 1,
    Resolved = 2
}

public sealed class SosAlert : AggregateRoot
{
    private readonly List<SosLocation> _locations = [];

    public Guid RunnerUserId { get; private set; }
    public Guid LinkedDeviceId { get; private set; }
    public Guid? EventId { get; private set; }
    public SosStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? AcknowledgedAtUtc { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }

    /// <summary>Expose list directly (IReadOnlyCollection) — tránh AsReadOnly() làm EF change-tracker rối.</summary>
    public IReadOnlyCollection<SosLocation> Locations => _locations;

    private SosAlert()
    {
    }

    /// <summary>Id do watch sinh (idempotent).</summary>
    public static SosAlert Open(
        Guid id,
        Guid runnerUserId,
        Guid linkedDeviceId,
        Guid? eventId,
        SosLocation firstLocation)
    {
        if (id == Guid.Empty || runnerUserId == Guid.Empty || linkedDeviceId == Guid.Empty)
        {
            throw new DomainException("SOS không hợp lệ.");
        }

        var alert = new SosAlert
        {
            Id = id,
            RunnerUserId = runnerUserId,
            LinkedDeviceId = linkedDeviceId,
            EventId = eventId == Guid.Empty ? null : eventId,
            Status = SosStatus.Open,
            CreatedAtUtc = DateTime.UtcNow
        };
        alert._locations.Add(firstLocation);
        return alert;
    }

    public void AddLocation(SosLocation location)
    {
        if (Status == SosStatus.Resolved)
        {
            throw new DomainException("SOS đã đóng.");
        }

        _locations.Add(location);
    }

    public void Acknowledge()
    {
        if (Status == SosStatus.Resolved)
        {
            throw new DomainException("SOS đã đóng.");
        }

        Status = SosStatus.Acknowledged;
        AcknowledgedAtUtc = DateTime.UtcNow;
    }

    public void Resolve()
    {
        Status = SosStatus.Resolved;
        ResolvedAtUtc = DateTime.UtcNow;
    }
}

public sealed class SosLocation : Entity
{
    public Guid SosAlertId { get; private set; }
    public double Lat { get; private set; }
    public double Lng { get; private set; }
    public double? Accuracy { get; private set; }
    public int? Battery { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }

    private SosLocation()
    {
    }

    public static SosLocation Create(
        Guid sosAlertId,
        double lat,
        double lng,
        double? accuracy,
        int? battery,
        DateTime recordedAtUtc)
    {
        if (lat is < -90 or > 90 || lng is < -180 or > 180)
        {
            throw new DomainException("Toạ độ không hợp lệ.");
        }

        if (accuracy is < 0)
        {
            throw new DomainException("accuracy không hợp lệ.");
        }

        if (battery is < 0 or > 100)
        {
            throw new DomainException("battery phải 0–100.");
        }

        var recorded = recordedAtUtc.Kind == DateTimeKind.Utc
            ? recordedAtUtc
            : DateTime.SpecifyKind(recordedAtUtc, DateTimeKind.Utc);

        return new SosLocation
        {
            Id = Guid.NewGuid(),
            SosAlertId = sosAlertId,
            Lat = lat,
            Lng = lng,
            Accuracy = accuracy,
            Battery = battery,
            RecordedAtUtc = recorded,
            ReceivedAtUtc = DateTime.UtcNow
        };
    }
}
