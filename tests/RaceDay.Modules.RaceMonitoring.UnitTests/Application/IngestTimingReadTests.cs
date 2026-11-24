using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Application.CheckpointScans;
using RaceDay.Modules.RaceMonitoring.Application.TimingReads;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Application;

public class IngestTimingReadTests
{
    private static readonly Guid EventId = Guid.Parse("b8fdd6c9-c6a6-44d9-9958-a98c8094fba8");
    private static readonly Guid CheckpointId = Guid.Parse("f1d10000-0000-4000-8000-000000000001");
    private static readonly Guid RegistrationId = Guid.Parse("44444444-dddd-4ddd-8ddd-ddddddddd000");
    private static readonly Guid DistanceId = Guid.Parse("3b89c7a3-7345-47b4-b7c9-50f1bbc28fa1");
    private const string ReaderId = "ypd-202-192.168.1.1";
    private const string Epc = "E200470833106026A8490109";
    private static readonly DateTime ObservedAt = new(2026, 11, 19, 4, 18, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Assigned_Tag_Writes_One_Timeline_Entry_At_The_Mapped_Checkpoint()
    {
        var harness = Harness();

        var result = await harness.Handler.Handle(Command(Epc, Guid.NewGuid(), "GatewayClock"), CancellationToken.None);

        Assert.Equal(CheckpointScanRecorder.AcceptedOutcome, result.Outcome);
        Assert.Equal(EventId, result.EventId);
        Assert.Equal(CheckpointId, result.CheckpointId);
        Assert.Equal(RegistrationId, result.RegistrationId);
        harness.Scans.Received(1).Add(Arg.Is<CheckpointScan>(scan =>
            scan.ScanMethod == ScanMethod.RFID
            && scan.CheckpointId == CheckpointId
            && scan.RegistrationId == RegistrationId));
        harness.Logs.Received(1).Add(Arg.Is<RaceEventLog>(log =>
            log.EventType == RaceEventLogType.RunnerCheckpointPassed
            && log.EventId == EventId
            && log.CheckpointId == CheckpointId
            && log.RegistrationId == RegistrationId
            && log.BibNumber == "50001"
            && log.SourceType == RaceEventLogSource.CheckpointScan));
        harness.Publisher.Received(1).Publish(Arg.Any<RaceEventLogNotification>());
    }

    [Fact]
    public async Task Unassigned_Tag_Is_Rejected_Before_The_Recorder()
    {
        var recorder = Substitute.For<ICheckpointScanRecorder>();
        var handler = new IngestTimingReadCommandHandler(
            new TimingReadResolver(DemoOptions(), new EmptyTagAssignments()),
            recorder,
            NullLogger<IngestTimingReadCommandHandler>.Instance);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            handler.Handle(Command("FFFFFFFFFFFFFFFFFFFFFFFF", Guid.NewGuid(), "TestClock"), CancellationToken.None));

        Assert.Equal("Thẻ chưa được gán cho vận động viên trong phạm vi giải này.", ex.Message);
        await recorder.DidNotReceive().RecordAsync(Arg.Any<CheckpointScanRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Same_Epc_Mapped_On_Another_Event_Is_Outside_This_Race()
    {
        var options = DemoOptions();
        options.Tags.Add(new RfidTagBinding
        {
            EventId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            TagScheme = "EPC",
            TagId = Epc,
            RegistrationId = Guid.Parse("99999999-9999-4999-8999-999999999999")
        });
        var assignment = await new TimingReadResolver(options, new EmptyTagAssignments())
            .ResolveAsync(ReaderId, "EPC", Epc);

        Assert.Equal(RegistrationId, assignment.RegistrationId);
        Assert.Equal(CheckpointId, assignment.CheckpointId);
        Assert.Equal(EventId, assignment.EventId);
    }

    [Fact]
    public async Task Database_Assignment_Is_Visible_Immediately_And_Repeated_ReadId_Stays_One_Row()
    {
        var otherRegistration = Guid.Parse("55555555-dddd-4ddd-8ddd-ddddddddd001");
        var assignedEpc = "E20099998888777766665555";
        var store = new ListTagAssignments();
        store.Rows.Add(RfidTagAssignment.Create(EventId, otherRegistration, assignedEpc, Guid.NewGuid(), ObservedAt));

        var resolvedNew = await new TimingReadResolver(DemoOptions(), store).ResolveAsync(ReaderId, "EPC", assignedEpc);
        var resolvedDemo = await new TimingReadResolver(DemoOptions(), store).ResolveAsync(ReaderId, "epc", Epc.ToLowerInvariant());
        Assert.Equal(otherRegistration, resolvedNew.RegistrationId);
        Assert.Equal(RegistrationId, resolvedDemo.RegistrationId);

        var overridden = new ListTagAssignments();
        overridden.Rows.Add(RfidTagAssignment.Create(EventId, otherRegistration, Epc, Guid.NewGuid(), ObservedAt));
        var dbWins = await new TimingReadResolver(DemoOptions(), overridden).ResolveAsync(ReaderId, "EPC", Epc);
        Assert.Equal(otherRegistration, dbWins.RegistrationId);

        var registrations = Substitute.For<IEventRegistrationRaceQuery>();
        registrations.GetByIdAsync(otherRegistration, Arg.Any<CancellationToken>())
            .Returns(new EventRegistrationRaceInfo(otherRegistration, EventId, DistanceId, "50002", "Confirmed"));
        var checkpoints = Substitute.For<IEventCheckpointQuery>();
        checkpoints.GetAsync(CheckpointId, Arg.Any<CancellationToken>())
            .Returns(new EventCheckpointInfo(CheckpointId, EventId, DistanceId));
        var scans = Substitute.For<ICheckpointScanRepository>();
        var logs = Substitute.For<IRaceEventLogRepository>();
        var unitOfWork = Substitute.For<IRaceMonitoringUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        var publisher = Substitute.For<IRaceEventLogPublisher>();
        var recorder = new CheckpointScanRecorder(
            registrations,
            checkpoints,
            scans,
            logs,
            unitOfWork,
            publisher,
            NullLogger<CheckpointScanRecorder>.Instance);
        var handler = new IngestTimingReadCommandHandler(
            new TimingReadResolver(DemoOptions(), store),
            recorder,
            NullLogger<IngestTimingReadCommandHandler>.Instance);
        var readId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        CheckpointScan? stored = null;
        scans.GetByIdAsync(readId, Arg.Any<CancellationToken>()).Returns(_ => stored);
        scans.When(repository => repository.Add(Arg.Any<CheckpointScan>()))
            .Do(call => stored = call.Arg<CheckpointScan>());

        var first = await handler.Handle(Command(assignedEpc, readId, "GatewayClock"), CancellationToken.None);
        var second = await handler.Handle(Command(assignedEpc, readId, "GatewayClock"), CancellationToken.None);

        Assert.Equal(CheckpointScanRecorder.AcceptedOutcome, first.Outcome);
        Assert.Equal(otherRegistration, first.RegistrationId);
        Assert.Equal(CheckpointScanRecorder.DuplicateOutcome, second.Outcome);
        logs.Received(1).Add(Arg.Is<RaceEventLog>(log => log.BibNumber == "50002" && log.RegistrationId == otherRegistration));
        publisher.Received(1).Publish(Arg.Any<RaceEventLogNotification>());
    }

    [Fact]
    public async Task Repeated_ReadId_Does_Not_Create_A_Second_Timeline_Entry()
    {
        var harness = Harness();
        var readId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        CheckpointScan? stored = null;
        harness.Scans.GetByIdAsync(readId, Arg.Any<CancellationToken>()).Returns(_ => stored);
        harness.Scans.When(scans => scans.Add(Arg.Any<CheckpointScan>()))
            .Do(call => stored = call.Arg<CheckpointScan>());

        var first = await harness.Handler.Handle(Command(Epc, readId, "TestClock"), CancellationToken.None);
        var second = await harness.Handler.Handle(Command(Epc, readId, "TestClock"), CancellationToken.None);

        Assert.Equal(CheckpointScanRecorder.AcceptedOutcome, first.Outcome);
        Assert.Equal(CheckpointScanRecorder.DuplicateOutcome, second.Outcome);
        Assert.Equal(readId, second.ScanId);
        harness.Logs.Received(1).Add(Arg.Any<RaceEventLog>());
        harness.Publisher.Received(1).Publish(Arg.Any<RaceEventLogNotification>());
    }

    [Fact]
    public async Task TestClock_And_GatewayClock_Use_The_Same_Recorder_Path()
    {
        var harness = Harness();

        await harness.Handler.Handle(Command(Epc, Guid.NewGuid(), "TestClock"), CancellationToken.None);
        await harness.Handler.Handle(Command(Epc, Guid.NewGuid(), "GatewayClock"), CancellationToken.None);

        harness.Scans.Received(2).Add(Arg.Is<CheckpointScan>(scan =>
            scan.ScanMethod == ScanMethod.RFID && scan.CheckpointId == CheckpointId));
        harness.Logs.Received(2).Add(Arg.Is<RaceEventLog>(log =>
            log.CheckpointId == CheckpointId && log.EventType == RaceEventLogType.RunnerCheckpointPassed));
    }

    [Fact]
    public void Service_Key_Must_Match_And_Empty_Config_Rejects_Every_Caller()
    {
        Assert.True(RfidGatewayAuthenticator.IsValid("dev-rfid-gateway-key", "dev-rfid-gateway-key"));
        Assert.False(RfidGatewayAuthenticator.IsValid("other-key", "dev-rfid-gateway-key"));
        Assert.False(RfidGatewayAuthenticator.IsValid("dev-rfid-gateway-key", ""));
        Assert.False(RfidGatewayAuthenticator.IsValid(null, "dev-rfid-gateway-key"));
    }

    private static IngestTimingReadCommand Command(string tagId, Guid readId, string timestampSource) => new(
        new TimingReadRequest(readId, ReaderId, "EPC", tagId, ObservedAt, timestampSource));

    private static RfidGatewayOptions DemoOptions() => new()
    {
        ServiceKey = "dev-rfid-gateway-key",
        Readers =
        [
            new RfidReaderBinding
            {
                ReaderId = ReaderId,
                EventId = EventId,
                CheckpointId = CheckpointId
            }
        ],
        Tags =
        [
            new RfidTagBinding
            {
                EventId = EventId,
                TagScheme = "EPC",
                TagId = Epc,
                RegistrationId = RegistrationId
            }
        ]
    };

    private static HarnessState Harness()
    {
        var registrations = Substitute.For<IEventRegistrationRaceQuery>();
        registrations.GetByIdAsync(RegistrationId, Arg.Any<CancellationToken>())
            .Returns(new EventRegistrationRaceInfo(RegistrationId, EventId, DistanceId, "50001", "Confirmed"));
        var checkpoints = Substitute.For<IEventCheckpointQuery>();
        checkpoints.GetAsync(CheckpointId, Arg.Any<CancellationToken>())
            .Returns(new EventCheckpointInfo(CheckpointId, EventId, DistanceId));
        var scans = Substitute.For<ICheckpointScanRepository>();
        var logs = Substitute.For<IRaceEventLogRepository>();
        var unitOfWork = Substitute.For<IRaceMonitoringUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        var publisher = Substitute.For<IRaceEventLogPublisher>();
        var recorder = new CheckpointScanRecorder(
            registrations,
            checkpoints,
            scans,
            logs,
            unitOfWork,
            publisher,
            NullLogger<CheckpointScanRecorder>.Instance);
        var handler = new IngestTimingReadCommandHandler(
            new TimingReadResolver(DemoOptions(), new EmptyTagAssignments()),
            recorder,
            NullLogger<IngestTimingReadCommandHandler>.Instance);
        return new HarnessState(handler, scans, logs, publisher);
    }

    private sealed class EmptyTagAssignments : IRfidTagAssignmentRepository
    {
        public Task<RfidTagAssignment?> FindByTagAsync(
            Guid eventId, string tagScheme, string tagId, CancellationToken cancellationToken = default)
            => Task.FromResult<RfidTagAssignment?>(null);

        public Task<RfidTagAssignment?> FindByRegistrationAsync(
            Guid eventId, Guid registrationId, CancellationToken cancellationToken = default)
            => Task.FromResult<RfidTagAssignment?>(null);

        public void Add(RfidTagAssignment assignment)
        {
        }
    }

    private sealed class ListTagAssignments : IRfidTagAssignmentRepository
    {
        public List<RfidTagAssignment> Rows { get; } = [];

        public Task<RfidTagAssignment?> FindByTagAsync(
            Guid eventId, string tagScheme, string tagId, CancellationToken cancellationToken = default)
            => Task.FromResult(Rows.FirstOrDefault(row =>
                row.EventId == eventId
                && string.Equals(row.TagScheme, tagScheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(row.TagId, tagId, StringComparison.OrdinalIgnoreCase)));

        public Task<RfidTagAssignment?> FindByRegistrationAsync(
            Guid eventId, Guid registrationId, CancellationToken cancellationToken = default)
            => Task.FromResult(Rows.FirstOrDefault(row => row.EventId == eventId && row.RegistrationId == registrationId));

        public void Add(RfidTagAssignment assignment) => Rows.Add(assignment);
    }

    private sealed record HarnessState(
        IngestTimingReadCommandHandler Handler,
        ICheckpointScanRepository Scans,
        IRaceEventLogRepository Logs,
        IRaceEventLogPublisher Publisher);
}
