using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Application.CutoffWarnings;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.RunnerMonitoring;
using RaceDay.Modules.RaceMonitoring.UnitTests.Domain;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Application;

public class CutoffWarningPassageResolverTests
{
    private static readonly DateTime Cutoff = new(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid RegistrationId = Guid.NewGuid();
    private static readonly Guid Cp1Id = Guid.NewGuid();

    [Fact]
    public async Task ScanAtMissedCheckpoint_ResolvesAndPublishes()
    {
        var (resolver, warning, unitOfWork, publisher) = Harness(scannedOrder: 1);
        var scanId = Guid.NewGuid();

        await resolver.ResolveAsync(RegistrationId, Cp1Id, scanId, CancellationToken.None);

        Assert.Equal(CutoffWarningResolution.PassedCheckpoint, warning.Resolution);
        Assert.Equal(scanId, warning.ResolvedByScanId);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        publisher.Received(1).Publish(Arg.Is<CutoffWarningNotification>(n =>
            n.Kind == CutoffWarningNotificationKind.Resolved && n.WarningId == warning.Id));
    }

    [Fact]
    public async Task ScanAtLaterCheckpoint_Resolves()
    {
        var (resolver, warning, _, _) = Harness(scannedOrder: 3);

        await resolver.ResolveAsync(RegistrationId, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(CutoffWarningResolution.PassedLaterCheckpoint, warning.Resolution);
    }

    [Fact]
    public async Task ScanAtEarlierCheckpoint_LeavesWarningActive()
    {
        var (resolver, warning, unitOfWork, publisher) = Harness(scannedOrder: 0);

        await resolver.ResolveAsync(RegistrationId, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(warning.IsActive);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        publisher.DidNotReceive().Publish(Arg.Any<CutoffWarningNotification>());
    }

    private static (CutoffWarningPassageResolver, CutoffWarning, IRaceMonitoringUnitOfWork, ICutoffWarningPublisher)
        Harness(int scannedOrder)
    {
        var warning = CutoffWarning.Raise(
            CutoffWarningTests.Target(RegistrationId, Cp1Id),
            Cutoff,
            null,
            Cutoff.AddMinutes(3));
        var warnings = Substitute.For<ICutoffWarningRepository>();
        warnings.ListActiveByRegistrationAsync(RegistrationId, Arg.Any<CancellationToken>()).Returns([warning]);
        var schedule = Substitute.For<IEventCutoffScheduleQuery>();
        schedule.GetCheckpointSortOrderAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(scannedOrder);
        var unitOfWork = Substitute.For<IRaceMonitoringUnitOfWork>();
        var publisher = Substitute.For<ICutoffWarningPublisher>();
        var resolver = new CutoffWarningPassageResolver(
            warnings,
            schedule,
            unitOfWork,
            publisher,
            new FixedClock(new DateTimeOffset(Cutoff.AddMinutes(10))),
            NullLogger<CutoffWarningPassageResolver>.Instance);
        return (resolver, warning, unitOfWork, publisher);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
