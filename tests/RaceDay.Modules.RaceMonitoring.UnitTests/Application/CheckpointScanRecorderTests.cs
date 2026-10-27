using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Application.CheckpointScans;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Application;

public class CheckpointScanRecorderTests
{
    [Fact]
    public async Task Accepted_Scan_Persists_Log_Then_Publishes()
    {
        var harness = Harness();
        var order = new List<string>();
        harness.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                order.Add("save");
                return 1;
            });
        harness.Publisher.When(publisher => publisher.Publish(Arg.Any<RaceEventLogNotification>()))
            .Do(_ => order.Add("publish"));

        var result = await harness.Recorder.RecordAsync(Sample(ScanMethod.Manual), CancellationToken.None);

        Assert.Equal(CheckpointScanRecorder.AcceptedOutcome, result.Outcome);
        Assert.Equal(new[] { "save", "publish" }, order);
        harness.Scans.Received(1).Add(Arg.Is<CheckpointScan>(scan => scan.ScanMethod == ScanMethod.Manual));
        harness.Logs.Received(1).Add(Arg.Is<RaceEventLog>(log =>
            log.EventType == RaceEventLogType.RunnerCheckpointPassed
            && log.BibNumber == "1024"
            && log.SourceType == RaceEventLogSource.CheckpointScan
            && log.OccurredAtUtc < log.RecordedAtUtc));
    }

    [Fact]
    public async Task Rfid_Uses_The_Same_Path_As_Manual()
    {
        var harness = Harness();
        harness.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

        await harness.Recorder.RecordAsync(Sample(ScanMethod.RFID), CancellationToken.None);

        harness.Scans.Received(1).Add(Arg.Is<CheckpointScan>(scan => scan.ScanMethod == ScanMethod.RFID));
        harness.Publisher.Received(1).Publish(Arg.Any<RaceEventLogNotification>());
    }

    [Fact]
    public async Task Second_Pass_Of_The_Same_Checkpoint_Is_Another_Scan()
    {
        var harness = Harness();
        harness.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        var first = Sample(ScanMethod.Manual);
        var second = first with { ScanId = Guid.NewGuid(), ScannedAtUtc = first.ScannedAtUtc.AddMinutes(20) };

        await harness.Recorder.RecordAsync(first, CancellationToken.None);
        await harness.Recorder.RecordAsync(second, CancellationToken.None);

        harness.Scans.Received(2).Add(Arg.Any<CheckpointScan>());
        harness.Logs.Received(2).Add(Arg.Any<RaceEventLog>());
    }

    [Fact]
    public async Task Duplicate_Scan_Id_Does_Not_Publish()
    {
        var harness = Harness();
        var scan = Sample(ScanMethod.Manual);
        harness.Scans.GetByIdAsync(scan.ScanId, Arg.Any<CancellationToken>())
            .Returns(CheckpointScan.Create(scan.RegistrationId, scan.CheckpointId, scan.ScanMethod, scan.ScannedAtUtc, scan.ScanId));

        var result = await harness.Recorder.RecordAsync(scan, CancellationToken.None);

        Assert.Equal(CheckpointScanRecorder.DuplicateOutcome, result.Outcome);
        harness.Scans.DidNotReceive().Add(Arg.Any<CheckpointScan>());
        await harness.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        harness.Publisher.DidNotReceive().Publish(Arg.Any<RaceEventLogNotification>());
    }

    [Fact]
    public async Task Concurrent_Unique_Conflict_Does_Not_Publish()
    {
        var harness = Harness();
        harness.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(0);

        var result = await harness.Recorder.RecordAsync(Sample(ScanMethod.Manual), CancellationToken.None);

        Assert.Equal(CheckpointScanRecorder.DuplicateOutcome, result.Outcome);
        harness.Publisher.DidNotReceive().Publish(Arg.Any<RaceEventLogNotification>());
    }

    [Fact]
    public async Task Sse_Failure_Keeps_The_Accepted_Result()
    {
        var harness = Harness();
        harness.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        harness.Publisher.When(publisher => publisher.Publish(Arg.Any<RaceEventLogNotification>()))
            .Do(_ => throw new InvalidOperationException("sse down"));

        var result = await harness.Recorder.RecordAsync(Sample(ScanMethod.Manual), CancellationToken.None);

        Assert.Equal(CheckpointScanRecorder.AcceptedOutcome, result.Outcome);
    }

    [Fact]
    public async Task Rejected_Registration_Does_Not_Persist()
    {
        var harness = Harness(status: "PendingApproval");

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            harness.Recorder.RecordAsync(Sample(ScanMethod.Manual), CancellationToken.None));

        Assert.Equal("Chỉ VĐV confirmed hoặc finished mới ghi nhận qua checkpoint.", ex.Message);
        harness.Scans.DidNotReceive().Add(Arg.Any<CheckpointScan>());
        await harness.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Checkpoint_On_Another_Distance_Does_Not_Persist()
    {
        var harness = Harness(checkpointDistanceId: Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            harness.Recorder.RecordAsync(Sample(ScanMethod.RFID), CancellationToken.None));

        Assert.Equal("Checkpoint không thuộc cự ly của VĐV.", ex.Message);
        harness.Logs.DidNotReceive().Add(Arg.Any<RaceEventLog>());
    }

    private static CheckpointScanRecord Sample(ScanMethod scanMethod) => new(
        Guid.NewGuid(),
        EventId,
        RegistrationId,
        CheckpointId,
        scanMethod,
        new DateTime(2026, 10, 27, 5, 18, 35, DateTimeKind.Utc));

    private static readonly Guid EventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RegistrationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CheckpointId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DistanceId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static HarnessState Harness(string status = "Confirmed", Guid? checkpointDistanceId = null)
    {
        var registrations = Substitute.For<IEventRegistrationRaceQuery>();
        registrations.GetByIdAsync(RegistrationId, Arg.Any<CancellationToken>())
            .Returns(new EventRegistrationRaceInfo(RegistrationId, EventId, DistanceId, "1024", status));
        var checkpoints = Substitute.For<IEventCheckpointQuery>();
        checkpoints.GetAsync(CheckpointId, Arg.Any<CancellationToken>())
            .Returns(new EventCheckpointInfo(CheckpointId, EventId, checkpointDistanceId ?? DistanceId));
        var scans = Substitute.For<ICheckpointScanRepository>();
        var logs = Substitute.For<IRaceEventLogRepository>();
        var unitOfWork = Substitute.For<IRaceMonitoringUnitOfWork>();
        var publisher = Substitute.For<IRaceEventLogPublisher>();
        var recorder = new CheckpointScanRecorder(
            registrations,
            checkpoints,
            scans,
            logs,
            unitOfWork,
            publisher,
            NullLogger<CheckpointScanRecorder>.Instance);
        return new HarnessState(recorder, scans, logs, unitOfWork, publisher);
    }

    private sealed record HarnessState(
        CheckpointScanRecorder Recorder,
        ICheckpointScanRepository Scans,
        IRaceEventLogRepository Logs,
        IRaceMonitoringUnitOfWork UnitOfWork,
        IRaceEventLogPublisher Publisher);
}
