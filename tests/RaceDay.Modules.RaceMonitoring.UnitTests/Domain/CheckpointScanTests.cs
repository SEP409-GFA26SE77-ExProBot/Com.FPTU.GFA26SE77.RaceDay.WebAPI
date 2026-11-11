using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Domain;

public class CheckpointScanTests
{
    private static readonly DateTime ScannedAt = new(2026, 10, 27, 5, 18, 35, DateTimeKind.Utc);

    [Fact]
    public void Create_Keeps_Generated_Id_When_Caller_Omits_It()
    {
        var scan = CheckpointScan.Create(Guid.NewGuid(), Guid.NewGuid(), ScanMethod.RFID, ScannedAt);

        Assert.NotEqual(Guid.Empty, scan.Id);
        Assert.Equal(ScanMethod.RFID, scan.ScanMethod);
        Assert.Equal(ScannedAt, scan.ScannedAtUtc);
    }

    [Fact]
    public void Create_Uses_Caller_Id_For_Both_Scan_Methods()
    {
        var id = Guid.NewGuid();
        var registrationId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid();

        var rfid = CheckpointScan.Create(registrationId, checkpointId, ScanMethod.RFID, ScannedAt, id);
        var manual = CheckpointScan.Create(registrationId, checkpointId, ScanMethod.Manual, ScannedAt, Guid.NewGuid());

        Assert.Equal(id, rfid.Id);
        Assert.Equal(registrationId, manual.RegistrationId);
        Assert.Equal(checkpointId, manual.CheckpointId);
    }

    [Fact]
    public void Create_Rejects_Empty_Id()
    {
        var ex = Assert.Throws<DomainException>(() =>
            CheckpointScan.Create(Guid.NewGuid(), Guid.NewGuid(), ScanMethod.Manual, ScannedAt, Guid.Empty));
        Assert.Equal("Mã lượt quét không hợp lệ.", ex.Message);
    }
}
