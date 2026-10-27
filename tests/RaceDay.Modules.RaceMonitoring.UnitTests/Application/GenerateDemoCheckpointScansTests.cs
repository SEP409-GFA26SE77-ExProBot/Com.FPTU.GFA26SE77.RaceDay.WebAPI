using NSubstitute;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Application.CheckpointScans;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Application;

public class GenerateDemoCheckpointScansTests
{
    [Fact]
    public async Task Demo_Calls_The_Recorder_With_Real_Pairs_Only()
    {
        var eventId = Guid.NewGuid();
        var distanceId = Guid.NewGuid();
        var otherDistanceId = Guid.NewGuid();
        var registrationId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid();
        var registrations = Substitute.For<IEventRegistrationRaceQuery>();
        registrations.ListEligibleByEventAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new EventRegistrationRaceCandidate(registrationId, eventId, distanceId, "A0001"),
                new EventRegistrationRaceCandidate(Guid.NewGuid(), eventId, otherDistanceId, "B0002")
            });
        var checkpoints = Substitute.For<IEventCheckpointQuery>();
        checkpoints.ListByEventAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(new[] { new EventCheckpointInfo(checkpointId, eventId, distanceId) });
        var recorder = Substitute.For<ICheckpointScanRecorder>();
        var recorded = new List<CheckpointScanRecord>();
        recorder.RecordAsync(Arg.Any<CheckpointScanRecord>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var scan = call.Arg<CheckpointScanRecord>();
                recorded.Add(scan);
                return new RecordCheckpointScanResult(
                    CheckpointScanRecorder.AcceptedOutcome,
                    scan.ScanId,
                    Guid.NewGuid(),
                    1,
                    eventId);
            });
        var user = Substitute.For<ICurrentUserAccessor>();
        user.UserId.Returns(Guid.NewGuid());
        var access = Substitute.For<IEventOrganizerAccessQuery>();
        access.GetAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(new EventOrganizerAccessInfo(eventId, Guid.NewGuid(), "Approved", "Demo"));
        var sos = Substitute.For<IEventSosOpsAccessQuery>();
        sos.CanManageSosAsync(eventId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        var handler = new GenerateDemoCheckpointScansCommandHandler(
            registrations, checkpoints, recorder, access, sos, user);

        var result = await handler.Handle(new GenerateDemoCheckpointScansCommand(eventId, 4), CancellationToken.None);

        Assert.Equal(4, result.Accepted);
        Assert.Equal(4, recorded.Select(scan => scan.ScanId).Distinct().Count());
        Assert.All(recorded, scan =>
        {
            Assert.Equal(eventId, scan.EventId);
            Assert.Equal(registrationId, scan.RegistrationId);
            Assert.Equal(checkpointId, scan.CheckpointId);
            Assert.True(Enum.IsDefined(scan.ScanMethod));
            Assert.Equal(DateTimeKind.Utc, scan.ScannedAtUtc.Kind);
        });
    }
}
