using NSubstitute;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Application.Rfid;
using RaceDay.Modules.RaceMonitoring.Application.TimingReads;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Application;

public class RfidAssignmentTests
{
    [Fact]
    public async Task Owner_Can_Read_And_Another_Organizer_Cannot()
    {
        var fx = new Fixture();
        var created = await fx.Create(fx.OwnerId);

        Assert.Equal(RfidReadStatuses.Pending, created.Status);
        Assert.Equal(fx.RegistrationA, created.RegistrationId);

        var denied = await Assert.ThrowsAsync<DomainException>(() => fx.Create(fx.OtherUserId));
        Assert.Equal("Bạn không phải chủ giải này.", denied.Message);
        await Assert.ThrowsAsync<DomainException>(() => fx.Create(userId: null));
        await Assert.ThrowsAsync<DomainException>(() => fx.Cancel(created.RequestId, fx.OtherUserId));
        await Assert.ThrowsAsync<DomainException>(() => fx.Confirm(created.RequestId, Fixture.FreshEpc, fx.OtherUserId));
        await Assert.ThrowsAsync<DomainException>(() => fx.Start(fx.OtherUserId));

        var admin = await fx.Create(fx.AdminId, admin: true);
        Assert.Equal(fx.ReaderB, admin.ReaderId);
    }

    [Fact]
    public async Task Only_One_Active_Request_Per_Reader_Including_Concurrent_Creates()
    {
        var fx = new Fixture();
        await fx.Create(fx.OwnerId);
        var blocked = await Assert.ThrowsAsync<RfidConflictException>(() => fx.Create(fx.OwnerId));
        Assert.Contains("chưa kết thúc", blocked.Message);

        var board = RfidReadRequestBoard.ForClock(new ManualTime());
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            try
            {
                board.Create(fx.EventId, fx.RegistrationB, fx.ReaderA, fx.OwnerId);
                return true;
            }
            catch (RfidConflictException)
            {
                return false;
            }
        })).ToArray();

        var results = await Task.WhenAll(tasks);
        Assert.Equal(1, results.Count(won => won));
    }

    [Fact]
    public async Task Cancel_And_Expiry_Release_The_Reader_Without_Confirming()
    {
        var fx = new Fixture();
        var created = await fx.Create(fx.OwnerId);
        fx.Board.ClaimNext(fx.ReaderA, created.RequestId);
        fx.Board.Observe(created.RequestId, fx.ReaderA, [Fixture.FreshEpc]);

        var cancelled = await fx.Cancel(created.RequestId, fx.OwnerId);
        Assert.Equal(RfidReadStatuses.Cancelled, cancelled.Status);
        await Assert.ThrowsAsync<DomainException>(() => fx.Confirm(created.RequestId, Fixture.FreshEpc, fx.OwnerId));
        var again = await fx.Create(fx.OwnerId);
        Assert.NotEqual(created.RequestId, again.RequestId);

        fx.Clock.Now = fx.Clock.Now.AddSeconds(61);
        var expiredPoll = fx.Board.ClaimNext(fx.ReaderA, again.RequestId);
        Assert.Equal(RfidReaderCommands.Checkpoint, expiredPoll.Command);
        Assert.Equal(RfidReadStatuses.Expired, expiredPoll.Status);
        var afterExpiry = await fx.Create(fx.OwnerId);
        Assert.Equal(RfidReadStatuses.Pending, afterExpiry.Status);
    }

    [Fact]
    public async Task No_Tag_And_Multiple_Epcs_Cannot_Be_Confirmed()
    {
        var fx = new Fixture();
        var created = await fx.CreateFor(fx.RegistrationB, fx.ReaderA);
        var claimed = fx.Board.ClaimNext(fx.ReaderA, null);
        Assert.Equal(RfidReadStatuses.Reading, claimed.Status);

        var empty = fx.Board.Observe(created.RequestId, fx.ReaderA, []);
        Assert.Equal(RfidReadStatuses.NoTag, empty.Status);
        await Assert.ThrowsAsync<DomainException>(() => fx.Confirm(created.RequestId, Fixture.FreshEpc, fx.OwnerId));

        var noisy = fx.Board.Observe(created.RequestId, fx.ReaderA, [Fixture.FreshEpc, Fixture.OtherEpc, Fixture.FreshEpc]);
        Assert.Equal(RfidReadStatuses.Ambiguous, noisy.Status);
        Assert.Equal(2, noisy.TagIds.Count);
        await Assert.ThrowsAsync<DomainException>(() => fx.Confirm(created.RequestId, Fixture.FreshEpc, fx.OwnerId));
        Assert.Empty(fx.Memory.Rows);

        Assert.Throws<DomainException>(() => fx.Board.Observe(created.RequestId, fx.ReaderB, [Fixture.FreshEpc]));
    }

    [Fact]
    public async Task Confirm_Stores_One_Epc_And_Same_Pair_Returns_The_Existing_Row()
    {
        var fx = new Fixture();
        var created = await fx.PrepareObserved(fx.RegistrationB, fx.ReaderA, Fixture.FreshEpc);
        var saved = await fx.Confirm(created.RequestId, Fixture.FreshEpc.ToLowerInvariant(), fx.OwnerId);

        Assert.True(saved.Created);
        Assert.Equal(Fixture.FreshEpc, saved.TagId);
        Assert.Equal(fx.RegistrationB, saved.RegistrationId);
        Assert.Single(fx.Memory.Rows);
        Assert.Equal(RfidReaderModes.AwaitCheckpoint, fx.Board.ModeOf(fx.ReaderA));

        var again = await fx.Confirm(created.RequestId, Fixture.FreshEpc, fx.OwnerId);
        Assert.False(again.Created);
        Assert.Equal(saved.AssignmentId, again.AssignmentId);
        Assert.Single(fx.Memory.Rows);

        var resolved = await new TimingReadResolver(fx.Options, fx.Memory)
            .ResolveAsync(fx.ReaderA, "EPC", Fixture.FreshEpc);
        Assert.Equal(fx.RegistrationB, resolved.RegistrationId);
    }

    [Fact]
    public async Task Conflicts_From_Database_And_Phase1_Config_Return_409_In_Both_Directions()
    {
        var fx = new Fixture();
        var configTag = await fx.PrepareObserved(fx.RegistrationB, fx.ReaderA, Fixture.DemoEpc);
        var stolenTag = await Assert.ThrowsAsync<RfidConflictException>(() =>
            fx.Confirm(configTag.RequestId, Fixture.DemoEpc, fx.OwnerId));
        Assert.Contains("cấu hình", stolenTag.Message);
        await fx.Cancel(configTag.RequestId, fx.OwnerId);

        var configRegistration = await fx.PrepareObserved(fx.RegistrationA, fx.ReaderA, Fixture.FreshEpc);
        var stolenRegistration = await Assert.ThrowsAsync<RfidConflictException>(() =>
            fx.Confirm(configRegistration.RequestId, Fixture.FreshEpc, fx.OwnerId));
        Assert.Contains("cấu hình", stolenRegistration.Message);
        await fx.Cancel(configRegistration.RequestId, fx.OwnerId);

        fx.Memory.Rows.Add(RfidTagAssignment.Create(
            fx.EventId, fx.RegistrationC, Fixture.OtherEpc, fx.OwnerId, DateTime.UtcNow));
        var dbTag = await fx.PrepareObserved(fx.RegistrationB, fx.ReaderA, Fixture.OtherEpc);
        var dbTagConflict = await Assert.ThrowsAsync<RfidConflictException>(() =>
            fx.Confirm(dbTag.RequestId, Fixture.OtherEpc, fx.OwnerId));
        Assert.Contains("đăng ký khác", dbTagConflict.Message);
        await fx.Cancel(dbTag.RequestId, fx.OwnerId);

        fx.Memory.Rows.Add(RfidTagAssignment.Create(
            fx.EventId, fx.RegistrationB, "AABBCCDDEEFF0011", fx.OwnerId, DateTime.UtcNow));
        var dbRegistration = await fx.PrepareObserved(fx.RegistrationB, fx.ReaderA, "AABBCCDDEEFF0022");
        var dbRegistrationConflict = await Assert.ThrowsAsync<RfidConflictException>(() =>
            fx.Confirm(dbRegistration.RequestId, "AABBCCDDEEFF0022", fx.OwnerId));
        Assert.Contains("EPC khác", dbRegistrationConflict.Message);
        Assert.Equal(2, fx.Memory.Rows.Count);
    }

    [Fact]
    public async Task Concurrent_Same_Pair_Keeps_One_Row_And_A_Different_Pair_Returns_409()
    {
        var same = new Fixture();
        var observed = await same.PrepareObserved(same.RegistrationB, same.ReaderA, Fixture.FreshEpc);
        var twins = await Task.WhenAll(
            Task.Run(() => same.Confirm(observed.RequestId, Fixture.FreshEpc, same.OwnerId)),
            Task.Run(() => same.Confirm(observed.RequestId, Fixture.FreshEpc, same.OwnerId)));
        Assert.Equal(twins[0].AssignmentId, twins[1].AssignmentId);
        Assert.Single(same.Memory.Rows);

        var race = new Fixture();
        var left = await race.PrepareObserved(race.RegistrationB, race.ReaderA, Fixture.FreshEpc);
        var right = await race.PrepareObserved(race.RegistrationC, race.ReaderB, Fixture.FreshEpc);
        var first = Task.Run(() => race.Confirm(left.RequestId, Fixture.FreshEpc, race.OwnerId));
        var second = Task.Run(() => race.Confirm(right.RequestId, Fixture.FreshEpc, race.OwnerId));
        try
        {
            await Task.WhenAll(first, second);
        }
        catch (Exception)
        {
        }

        Assert.Equal(1, new[] { first, second }.Count(task => task.Status == TaskStatus.RanToCompletion));
        var lost = new[] { first, second }.Single(task => task.IsFaulted);
        Assert.IsType<RfidConflictException>(lost.Exception!.InnerException);
        Assert.Single(race.Memory.Rows);
    }

    [Fact]
    public async Task Confirmed_Assignment_Holds_Checkpoint_Scan_Until_The_Owner_Starts_It()
    {
        var fx = new Fixture();
        var created = await fx.PrepareObserved(fx.RegistrationB, fx.ReaderA, Fixture.FreshEpc);
        await fx.Confirm(created.RequestId, Fixture.FreshEpc, fx.OwnerId);

        var hold = fx.Board.ClaimNext(fx.ReaderA, created.RequestId);
        Assert.Equal(RfidReaderCommands.Hold, hold.Command);
        Assert.Contains(Fixture.FreshEpc, hold.ReleaseTagIds);

        var started = await fx.Start(fx.OwnerId);
        Assert.Equal(RfidReaderModes.CheckpointScan, started.Mode);
        var scan = fx.Board.ClaimNext(fx.ReaderA, created.RequestId);
        Assert.Equal(RfidReaderCommands.Checkpoint, scan.Command);
        Assert.Contains(Fixture.FreshEpc, scan.ReleaseTagIds);
        var following = fx.Board.ClaimNext(fx.ReaderA, null);
        Assert.DoesNotContain(Fixture.FreshEpc, following.ReleaseTagIds);
    }

    private sealed class Fixture
    {
        public static string DemoEpc => "E200470833106026A8490109";
        public static string FreshEpc => "E20011112222333344445555";
        public static string OtherEpc => "E200AAAAAAAAAAAAAAAABBBB";

        public Guid EventId { get; } = Guid.Parse("b8fdd6c9-c6a6-44d9-9958-a98c8094fba8");
        public Guid OtherEventId { get; } = Guid.Parse("cccccccc-cccc-4ccc-8ccc-ccccccccccc1");
        public Guid CheckpointId { get; } = Guid.Parse("f1d10000-0000-4000-8000-000000000001");
        public Guid RegistrationA { get; } = Guid.Parse("44444444-dddd-4ddd-8ddd-ddddddddd000");
        public Guid RegistrationB { get; } = Guid.Parse("55555555-dddd-4ddd-8ddd-ddddddddd001");
        public Guid RegistrationC { get; } = Guid.Parse("66666666-dddd-4ddd-8ddd-ddddddddd002");
        public Guid OwnerId { get; } = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1");
        public Guid OtherUserId { get; } = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb2");
        public Guid AdminId { get; } = Guid.Parse("cccccccc-cccc-4ccc-8ccc-ccccccccccc3");
        public string ReaderA { get; } = "ypd-202-192.168.1.1";
        public string ReaderB { get; } = "ypd-202-192.168.1.2";
        public ManualTime Clock { get; } = new();
        public RfidReadRequestBoard Board { get; }
        public RfidAssignmentMemory Memory { get; } = new();
        public RfidGatewayOptions Options { get; }
        public IEventOrganizerAccessQuery Access { get; } = Substitute.For<IEventOrganizerAccessQuery>();
        public IEventRegistrationRaceQuery Registrations { get; } = Substitute.For<IEventRegistrationRaceQuery>();

        public Fixture()
        {
            Board = RfidReadRequestBoard.ForClock(Clock);
            Options = new RfidGatewayOptions
            {
                ServiceKey = "dev-rfid-gateway-key",
                Readers =
                [
                    new RfidReaderBinding { ReaderId = ReaderA, EventId = EventId, CheckpointId = CheckpointId },
                    new RfidReaderBinding
                    {
                        ReaderId = ReaderB,
                        EventId = EventId,
                        CheckpointId = Guid.Parse("f1d10000-0000-4000-8000-000000000002")
                    }
                ],
                Tags =
                [
                    new RfidTagBinding
                    {
                        EventId = EventId,
                        TagScheme = "EPC",
                        TagId = DemoEpc,
                        RegistrationId = RegistrationA
                    }
                ]
            };
            Access.GetAsync(EventId, Arg.Any<CancellationToken>())
                .Returns(new EventOrganizerAccessInfo(EventId, OwnerId, "RegistrationOpen", "Demo"));
            Access.GetAsync(OtherEventId, Arg.Any<CancellationToken>())
                .Returns((EventOrganizerAccessInfo?)null);
            foreach (var registrationId in new[] { RegistrationA, RegistrationB, RegistrationC })
            {
                Registrations.GetByIdAsync(registrationId, Arg.Any<CancellationToken>())
                    .Returns(new EventRegistrationRaceInfo(registrationId, EventId, Guid.NewGuid(), "100", "Confirmed"));
            }
        }

        public Task<RfidReadRequestDto> Create(Guid? userId, bool admin = false) =>
            CreateHandler(userId, admin).Handle(
                new CreateRfidReadRequestCommand(EventId, userId == AdminId ? RegistrationC : RegistrationA, userId == AdminId ? ReaderB : ReaderA),
                CancellationToken.None);

        public async Task<RfidReadRequestDto> CreateFor(Guid registrationId, string readerId)
        {
            var created = await CreateHandler(OwnerId, false).Handle(
                new CreateRfidReadRequestCommand(EventId, registrationId, readerId),
                CancellationToken.None);
            return created;
        }

        public Task<RfidReadRequestDto> Cancel(Guid requestId, Guid userId) =>
            new CancelRfidReadRequestCommandHandler(Board, Access, User(userId, false)).Handle(
                new CancelRfidReadRequestCommand(requestId),
                CancellationToken.None);

        public Task<RfidAssignmentDto> Confirm(Guid requestId, string tagId, Guid userId) =>
            new ConfirmRfidAssignmentCommandHandler(
                Board, Options, Memory, Memory, Access, Registrations, User(userId, false)).Handle(
                new ConfirmRfidAssignmentCommand(requestId, tagId),
                CancellationToken.None);

        public Task<RfidReaderModeDto> Start(Guid userId) =>
            new StartRfidCheckpointScanCommandHandler(Board, Options, Access, User(userId, false)).Handle(
                new StartRfidCheckpointScanCommand(ReaderA),
                CancellationToken.None);

        public async Task<RfidReadRequestDto> PrepareObserved(Guid registrationId, string readerId, string epc)
        {
            var created = await CreateFor(registrationId, readerId);
            Board.ClaimNext(readerId, created.RequestId);
            return Board.Observe(created.RequestId, readerId, [epc]);
        }

        private CreateRfidReadRequestCommandHandler CreateHandler(Guid? userId, bool admin) =>
            new(Board, Options, Access, Registrations, User(userId, admin));

        private static ICurrentUserAccessor User(Guid? userId, bool admin) => new TestUser(userId, admin);

        private sealed class TestUser(Guid? userId, bool admin) : ICurrentUserAccessor
        {
            public Guid? UserId { get; } = userId;

            public bool IsInRole(string role) => admin && string.Equals(role, "Admin", StringComparison.Ordinal);
        }
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 24, 4, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RfidAssignmentMemory : IRfidTagAssignmentRepository, IRaceMonitoringUnitOfWork
    {
        private readonly object _gate = new();
        private readonly AsyncLocal<RfidTagAssignment?> _pending = new();
        public List<RfidTagAssignment> Rows { get; } = [];

        public Task<RfidTagAssignment?> FindByTagAsync(
            Guid eventId, string tagScheme, string tagId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult(Rows.FirstOrDefault(row =>
                    row.EventId == eventId
                    && string.Equals(row.TagScheme, tagScheme, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(row.TagId, tagId, StringComparison.OrdinalIgnoreCase)));
            }
        }

        public Task<RfidTagAssignment?> FindByRegistrationAsync(
            Guid eventId, Guid registrationId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult(Rows.FirstOrDefault(row =>
                    row.EventId == eventId && row.RegistrationId == registrationId));
            }
        }

        public void Add(RfidTagAssignment assignment) => _pending.Value = assignment;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var pending = _pending.Value ?? throw new InvalidOperationException("Không có bản ghi để lưu.");
            _pending.Value = null;
            lock (_gate)
            {
                var tagTaken = Rows.Any(row =>
                    row.EventId == pending.EventId
                    && string.Equals(row.TagScheme, pending.TagScheme, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(row.TagId, pending.TagId, StringComparison.OrdinalIgnoreCase));
                var registrationTaken = Rows.Any(row =>
                    row.EventId == pending.EventId && row.RegistrationId == pending.RegistrationId);
                if (tagTaken || registrationTaken)
                {
                    throw new RfidConflictException("EPC hoặc đăng ký đã được gán trong giải này.");
                }

                Rows.Add(pending);
            }

            return Task.FromResult(1);
        }
    }
}
