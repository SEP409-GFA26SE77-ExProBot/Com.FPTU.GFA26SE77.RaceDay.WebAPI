using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Domain;

public sealed class RunnerLocation : AggregateRoot
{
    public Guid RegistrationId { get; private set; }
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }

    private RunnerLocation()
    {
    }

    public static RunnerLocation Create(
        Guid registrationId,
        double latitude,
        double longitude,
        DateTime recordedAtUtc,
        DateTime receivedAtUtc)
    {
        if (registrationId == Guid.Empty)
        {
            throw new DomainException("Tham chiếu đăng ký không hợp lệ.");
        }

        EnsureCoordinates(latitude, longitude);

        if (recordedAtUtc == default)
        {
            throw new DomainException("Thời điểm ghi nhận vị trí không hợp lệ.");
        }

        if (receivedAtUtc == default)
        {
            throw new DomainException("Thời điểm nhận vị trí không hợp lệ.");
        }

        return new RunnerLocation
        {
            RegistrationId = registrationId,
            Latitude = latitude,
            Longitude = longitude,
            RecordedAtUtc = recordedAtUtc,
            ReceivedAtUtc = receivedAtUtc
        };
    }

    internal static void EnsureCoordinates(double latitude, double longitude)
    {
        if (latitude is < -90 or > 90)
        {
            throw new DomainException("Vĩ độ phải nằm trong khoảng -90 đến 90.");
        }

        if (longitude is < -180 or > 180)
        {
            throw new DomainException("Kinh độ phải nằm trong khoảng -180 đến 180.");
        }
    }
}
