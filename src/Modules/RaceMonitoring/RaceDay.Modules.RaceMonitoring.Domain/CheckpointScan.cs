using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Domain;

public sealed class CheckpointScan : AggregateRoot
{
    public Guid RegistrationId { get; private set; }
    public Guid CheckpointId { get; private set; }
    public DateTime ScannedAtUtc { get; private set; }
    public ScanMethod ScanMethod { get; private set; }

    private CheckpointScan()
    {
    }

    public static CheckpointScan Create(
        Guid registrationId,
        Guid checkpointId,
        ScanMethod scanMethod,
        DateTime scannedAtUtc)
    {
        if (registrationId == Guid.Empty || checkpointId == Guid.Empty)
        {
            throw new DomainException("Tham chiếu lượt quét checkpoint không hợp lệ.");
        }

        if (!Enum.IsDefined(scanMethod))
        {
            throw new DomainException("Phương thức quét không hợp lệ.");
        }

        if (scannedAtUtc == default)
        {
            throw new DomainException("Thời điểm quét checkpoint không hợp lệ.");
        }

        return new CheckpointScan
        {
            RegistrationId = registrationId,
            CheckpointId = checkpointId,
            ScanMethod = scanMethod,
            ScannedAtUtc = scannedAtUtc
        };
    }
}
