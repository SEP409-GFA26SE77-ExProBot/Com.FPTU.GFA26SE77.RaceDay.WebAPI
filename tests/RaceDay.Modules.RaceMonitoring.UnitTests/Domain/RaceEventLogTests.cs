using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Domain;

public class RaceEventLogTests
{
    [Fact]
    public void RecordCheckpointPassed_Stores_Occurrence_And_Source()
    {
        var occurredAt = new DateTime(2026, 10, 27, 5, 18, 35, DateTimeKind.Utc);
        var recordedAt = occurredAt.AddSeconds(2);
        var scanId = Guid.NewGuid();

        var log = RaceEventLog.RecordCheckpointPassed(
            Guid.NewGuid(),
            Guid.NewGuid(),
            occurredAt,
            recordedAt,
            "1024",
            Guid.NewGuid(),
            Guid.NewGuid(),
            scanId,
            """{"scanMethod":"RFID"}""");

        Assert.Equal(RaceEventLogType.RunnerCheckpointPassed, log.EventType);
        Assert.Equal(occurredAt, log.OccurredAtUtc);
        Assert.Equal(recordedAt, log.RecordedAtUtc);
        Assert.Equal("1024", log.BibNumber);
        Assert.Equal(scanId, log.SourceId);
        Assert.Equal(RaceEventLogSource.CheckpointScan, log.SourceType);
        Assert.Equal(0, log.Sequence);
    }

    [Fact]
    public void RecordCheckpointPassed_Rejects_Missing_Bib()
    {
        var ex = Assert.Throws<DomainException>(() => Create("  "));
        Assert.Equal("Thiếu số BIB.", ex.Message);
    }

    private static RaceEventLog Create(string bib)
    {
        var at = new DateTime(2026, 10, 27, 5, 0, 0, DateTimeKind.Utc);
        return RaceEventLog.RecordCheckpointPassed(
            Guid.NewGuid(),
            Guid.NewGuid(),
            at,
            at,
            bib,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null);
    }
}
