using NSubstitute;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Application.CheckpointScans;
using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Application;

public class CheckpointMonitoringQueriesTests
{
    [Fact]
    public async Task Stats_Returns_Total_Passed_And_NotPassed_For_Distance()
    {
        var eventId = Guid.NewGuid();
        var distanceId = Guid.NewGuid();
        var otherDistanceId = Guid.NewGuid();

        var registrations = Substitute.For<IEventRegistrationRaceQuery>();
        registrations.ListEligibleByEventAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new EventRegistrationRaceCandidate(
                    Guid.NewGuid(),
                    eventId,
                    distanceId,
                    "00125",
                    "Nguyen Van A",
                    "Finished",
                    new DateTime(2026, 11, 10, 8, 30, 15, DateTimeKind.Utc)),
                new EventRegistrationRaceCandidate(
                    Guid.NewGuid(),
                    eventId,
                    distanceId,
                    "00126",
                    "Tran Van B",
                    "Confirmed",
                    null),
                new EventRegistrationRaceCandidate(
                    Guid.NewGuid(),
                    eventId,
                    otherDistanceId,
                    "00127",
                    "Le Van C",
                    "Finished",
                    new DateTime(2026, 11, 10, 8, 35, 0, DateTimeKind.Utc))
            });

        var distances = Substitute.For<IEventDistanceAccessQuery>();
        distances.GetAsync(eventId, distanceId, Arg.Any<CancellationToken>())
            .Returns(new EventDistanceAccessInfo(eventId, distanceId, Guid.NewGuid(), "10KM", 10, "Approved"));

        var handler = new GetCheckpointMonitoringStatsQueryHandler(
            registrations,
            distances,
            OrganizerAccess(eventId),
            SosAccess(eventId),
            CurrentUser());

        var result = await handler.Handle(new GetCheckpointMonitoringStatsQuery(eventId, distanceId), CancellationToken.None);

        Assert.Equal(eventId, result.EventId);
        Assert.Equal(distanceId, result.DistanceId);
        Assert.Equal(2, result.TotalRunners);
        Assert.Equal(1, result.PassedCount);
        Assert.Equal(1, result.NotPassedCount);
    }

    [Fact]
    public async Task Runner_List_Can_Filter_NotPassed_And_Search_By_Name()
    {
        var eventId = Guid.NewGuid();
        var distanceId = Guid.NewGuid();
        var reg1 = Guid.NewGuid();
        var reg2 = Guid.NewGuid();
        var reg3 = Guid.NewGuid();

        var registrations = Substitute.For<IEventRegistrationRaceQuery>();
        registrations.ListEligibleByEventAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new EventRegistrationRaceCandidate(
                    reg1,
                    eventId,
                    distanceId,
                    "00125",
                    "Nguyen Van A",
                    "Finished",
                    new DateTime(2026, 11, 10, 8, 30, 15, DateTimeKind.Utc)),
                new EventRegistrationRaceCandidate(
                    reg2,
                    eventId,
                    distanceId,
                    "00126",
                    "Tran Van B",
                    "Finished",
                    new DateTime(2026, 11, 10, 8, 32, 10, DateTimeKind.Utc)),
                new EventRegistrationRaceCandidate(
                    reg3,
                    eventId,
                    distanceId,
                    "00127",
                    "Le Van C",
                    "Confirmed",
                    null)
            });

        var distances = Substitute.For<IEventDistanceAccessQuery>();
        distances.GetAsync(eventId, distanceId, Arg.Any<CancellationToken>())
            .Returns(new EventDistanceAccessInfo(eventId, distanceId, Guid.NewGuid(), "10KM", 10, "Approved"));

        var handler = new GetCheckpointMonitoringRunnersQueryHandler(
            registrations,
            distances,
            OrganizerAccess(eventId),
            SosAccess(eventId),
            CurrentUser());

        var result = await handler.Handle(
            new GetCheckpointMonitoringRunnersQuery(
                eventId,
                distanceId,
                "NotPassed",
                "Le Van",
                1,
                20),
            CancellationToken.None);

        Assert.Equal(eventId, result.EventId);
        Assert.Equal(distanceId, result.DistanceId);
        Assert.Equal(1, result.Pagination.TotalItems);
        Assert.Single(result.Items);
        Assert.Equal(reg3, result.Items[0].RegistrationId);
        Assert.Equal("00127", result.Items[0].BibNumber);
        Assert.Equal("Le Van C", result.Items[0].RunnerName);
        Assert.Equal("NotPassed", result.Items[0].Status);
        Assert.Null(result.Items[0].PassageTime);
    }

    private static ICurrentUserAccessor CurrentUser()
    {
        var user = Substitute.For<ICurrentUserAccessor>();
        user.UserId.Returns(Guid.NewGuid());
        return user;
    }

    private static IEventOrganizerAccessQuery OrganizerAccess(Guid eventId)
    {
        var access = Substitute.For<IEventOrganizerAccessQuery>();
        access.GetAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(new EventOrganizerAccessInfo(eventId, Guid.NewGuid(), "Approved", "Race"));
        return access;
    }

    private static IEventSosOpsAccessQuery SosAccess(Guid eventId)
    {
        var sos = Substitute.For<IEventSosOpsAccessQuery>();
        sos.CanManageSosAsync(eventId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        return sos;
    }
}
