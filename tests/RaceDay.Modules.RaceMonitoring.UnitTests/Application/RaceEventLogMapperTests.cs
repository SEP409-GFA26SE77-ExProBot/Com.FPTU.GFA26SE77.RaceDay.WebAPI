using RaceDay.Modules.RaceMonitoring.Application.RaceEventLogs;
using RaceDay.Modules.RaceMonitoring.Domain;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Application;

[Trait("Workflow", "WF-02")]
[Trait("Feature", "MF-07")]
public class RaceEventLogMapperTests
{
    [Fact]
    public void Checkpoint_Metadata_Round_Trips_Names()
    {
        var json = RaceEventLogMapper.SerializeCheckpointMetadata("RFID", " Checkpoint 1 (KM 5) ", " Nguyễn Văn A ");

        var metadata = RaceEventLogMapper.ReadMetadata(json);

        Assert.Equal("RFID", metadata.ScanMethod);
        Assert.Equal("Checkpoint 1 (KM 5)", metadata.CheckpointName);
        Assert.Equal("Nguyễn Văn A", metadata.RunnerName);
    }

    [Fact]
    public void Checkpoint_Metadata_Keeps_Vietnamese_Unescaped()
    {
        var json = RaceEventLogMapper.SerializeCheckpointMetadata("Manual", "Trạm nước", "Trần Thị Bích");

        Assert.Contains("Trần Thị Bích", json);
        Assert.Contains("Trạm nước", json);
    }

    [Fact]
    public void Checkpoint_Metadata_Omits_Blank_Names()
    {
        var json = RaceEventLogMapper.SerializeCheckpointMetadata("Manual", "  ", null);

        Assert.Equal("""{"scanMethod":"Manual"}""", json);
    }

    [Fact]
    public void Legacy_Metadata_Without_Names_Still_Reads_Scan_Method()
    {
        var metadata = RaceEventLogMapper.ReadMetadata("""{"scanMethod":"RFID"}""");

        Assert.Equal("RFID", metadata.ScanMethod);
        Assert.Null(metadata.CheckpointName);
        Assert.Null(metadata.RunnerName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("""{"scanMethod":5}""")]
    public void Bad_Metadata_Returns_Empty(string? raw)
    {
        var metadata = RaceEventLogMapper.ReadMetadata(raw);

        Assert.Null(metadata.ScanMethod);
        Assert.Null(metadata.CheckpointName);
        Assert.Null(metadata.RunnerName);
    }

    [Fact]
    public void ToNotification_Maps_Names_From_Metadata()
    {
        var log = RaceEventLog.RecordCheckpointPassed(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTime(2026, 10, 27, 3, 30, 15, DateTimeKind.Utc),
            new DateTime(2026, 10, 27, 3, 30, 16, DateTimeKind.Utc),
            "5110",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            RaceEventLogMapper.SerializeCheckpointMetadata("RFID", "Checkpoint 1 (KM 5)", "Nguyễn Văn A"));

        var notification = RaceEventLogMapper.ToNotification(log);

        Assert.Equal("5110", notification.BibNumber);
        Assert.Equal("RFID", notification.ScanMethod);
        Assert.Equal("Checkpoint 1 (KM 5)", notification.CheckpointName);
        Assert.Equal("Nguyễn Văn A", notification.RunnerName);
    }
}
