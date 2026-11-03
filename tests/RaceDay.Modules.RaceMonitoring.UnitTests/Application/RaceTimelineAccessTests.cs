using NSubstitute;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Application.CheckpointScans;
using RaceDay.Modules.RaceMonitoring.Application.RaceEventLogs;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Application;

public class RaceTimelineAccessTests
{
    [Fact]
    public async Task Organizer_Or_Active_Coordinator_Can_List()
    {
        var (handler, logs, _) = Handler(allowed: true);

        var result = await handler.Handle(new ListRaceEventLogsQuery(EventId, null, null, null, null, null, null), CancellationToken.None);

        Assert.Empty(result.Items);
        await logs.Received(1).ListAsync(Arg.Any<RaceEventLogFilter>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Admin_Does_Not_Bypass_Timeline_Access()
    {
        var (handler, logs, currentUser) = Handler(allowed: false);
        currentUser.IsInRole("Admin").Returns(true);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            handler.Handle(new ListRaceEventLogsQuery(EventId, null, null, null, null, null, null), CancellationToken.None));

        Assert.Equal("Chỉ chủ giải hoặc Coordinator Active mới xem timeline.", ex.Message);
        await logs.DidNotReceive().ListAsync(Arg.Any<RaceEventLogFilter>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Detail_Rejects_Log_From_Another_Event()
    {
        var logs = Substitute.For<IRaceEventLogRepository>();
        var otherEvent = Guid.NewGuid();
        logs.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(RaceEventLog.RecordCheckpointPassed(
                otherEvent,
                Guid.NewGuid(),
                DateTime.UtcNow,
                DateTime.UtcNow,
                "1024",
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                null));
        var handler = new GetRaceEventLogQueryHandler(logs, Access(allowed: true), Sos(allowed: true), User());

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            handler.Handle(new GetRaceEventLogQuery(EventId, Guid.NewGuid()), CancellationToken.None));

        Assert.Equal("Không tìm thấy log.", ex.Message);
    }

    [Fact]
    public async Task Record_And_View_Use_Separate_Denial_Messages()
    {
        var recorder = Substitute.For<ICheckpointScanRecorder>();
        var handler = new RecordCheckpointScanCommandHandler(recorder, Access(false), Sos(false), User());

        var ex = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new RecordCheckpointScanCommand(
                EventId,
                new RecordCheckpointScanRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Manual", DateTime.UtcNow)),
            CancellationToken.None));

        Assert.Equal("Chỉ chủ giải hoặc Coordinator Active mới ghi nhận checkpoint.", ex.Message);
        await recorder.DidNotReceive().RecordAsync(Arg.Any<CheckpointScanRecord>(), Arg.Any<CancellationToken>());
    }

    private static readonly Guid EventId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static (ListRaceEventLogsQueryHandler Handler, IRaceEventLogRepository Logs, ICurrentUserAccessor User) Handler(bool allowed)
    {
        var logs = Substitute.For<IRaceEventLogRepository>();
        logs.ListAsync(Arg.Any<RaceEventLogFilter>(), Arg.Any<CancellationToken>()).Returns((Array.Empty<RaceEventLog>(), 0));
        var user = User();
        return (new ListRaceEventLogsQueryHandler(logs, Access(allowed), Sos(allowed), user), logs, user);
    }

    private static ICurrentUserAccessor User()
    {
        var user = Substitute.For<ICurrentUserAccessor>();
        user.UserId.Returns(Guid.NewGuid());
        return user;
    }

    private static IEventOrganizerAccessQuery Access(bool allowed)
    {
        var access = Substitute.For<IEventOrganizerAccessQuery>();
        _ = allowed;
        access.GetAsync(EventId, Arg.Any<CancellationToken>())
            .Returns(new EventOrganizerAccessInfo(EventId, Guid.NewGuid(), "Approved", "Demo"));
        return access;
    }

    private static IEventSosOpsAccessQuery Sos(bool allowed)
    {
        var sos = Substitute.For<IEventSosOpsAccessQuery>();
        sos.CanManageSosAsync(EventId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(allowed);
        return sos;
    }
}
