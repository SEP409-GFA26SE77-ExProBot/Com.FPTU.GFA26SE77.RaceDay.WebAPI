using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Domain;

public sealed class SOSAlert : AggregateRoot
{
    public Guid RegistrationId { get; private set; }

    
    public Guid? RunnerLocationId { get; private set; }

    public RunnerLocation? RunnerLocation { get; private set; }
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public DateTime TriggeredAtUtc { get; private set; }
    public SOSAlertStatus Status { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }

    private SOSAlert()
    {
    }

    public static SOSAlert Create(
        Guid registrationId,
        DateTime triggeredAtUtc,
        Guid? runnerLocationId = null,
        double? latitude = null,
        double? longitude = null)
    {
        if (registrationId == Guid.Empty)
        {
            throw new DomainException("Tham chiếu đăng ký không hợp lệ.");
        }

        if (runnerLocationId == Guid.Empty)
        {
            throw new DomainException("Tham chiếu vị trí runner không hợp lệ.");
        }

        EnsureOptionalCoordinates(latitude, longitude);

        if (triggeredAtUtc == default)
        {
            throw new DomainException("Thời điểm kích hoạt SOS không hợp lệ.");
        }

        return new SOSAlert
        {
            RegistrationId = registrationId,
            RunnerLocationId = runnerLocationId,
            Latitude = latitude,
            Longitude = longitude,
            TriggeredAtUtc = triggeredAtUtc,
            Status = SOSAlertStatus.Active,
            ResolvedAtUtc = null
        };
    }

    public void Acknowledge()
    {
        if (Status != SOSAlertStatus.Active)
        {
            throw new DomainException("Chỉ SOS đang hoạt động mới được tiếp nhận.");
        }

        Status = SOSAlertStatus.Acknowledged;
    }

    public void Resolve(DateTime resolvedAtUtc)
    {
        if (Status is not (SOSAlertStatus.Active or SOSAlertStatus.Acknowledged))
        {
            throw new DomainException("SOS đã được xử lý.");
        }

        if (resolvedAtUtc == default || resolvedAtUtc < TriggeredAtUtc)
        {
            throw new DomainException("Thời điểm xử lý SOS không hợp lệ.");
        }

        Status = SOSAlertStatus.Resolved;
        ResolvedAtUtc = resolvedAtUtc;
    }

    private static void EnsureOptionalCoordinates(double? latitude, double? longitude)
    {
        if (latitude is null && longitude is null)
        {
            return;
        }

        if (latitude is null || longitude is null)
        {
            throw new DomainException("Tọa độ SOS phải có đủ vĩ độ và kinh độ.");
        }

        RunnerLocation.EnsureCoordinates(latitude.Value, longitude.Value);
    }
}
