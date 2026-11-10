using System.Text.Json;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Contracts.Simulation;
using RaceDay.Modules.Simulation.Application;
using RaceDay.Modules.Simulation.Contracts;
using RaceDay.Modules.Simulation.Domain;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Simulation.UnitTests;

public class SimulationSessionBookTests
{
    private readonly Guid _eventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _distanceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly Guid _a = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private readonly Guid _b = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");

    [Fact]
    public async Task Two_creates_share_one_run_and_reads_do_not_bump_version()
    {
        var (book, _, _) = NewBook();
        var first = await book.CreateAsync(_eventId, _distanceId, null, 42);
        var second = await book.CreateAsync(_eventId, _distanceId, 1, 99);
        Assert.Equal(first.RunId, second.RunId);
        Assert.Equal(42, second.Seed);
        Assert.Equal(1, second.Generation);
        Assert.Equal(1, second.Version);
        var again = book.SnapshotOf(first.RunId);
        Assert.Equal(1, again!.Version);
        Assert.Equal(_eventId, book.EventIdOf(first.RunId));
    }

    [Fact]
    public async Task Start_pause_resume_and_reset_keep_seed_and_raise_generation()
    {
        var (book, _, _) = NewBook();
        var created = await book.CreateAsync(_eventId, _distanceId, null, 42);
        var started = await book.StartAsync(created.RunId);
        Assert.Equal("Running", started.Phase);
        Assert.Equal(2, started.Version);
        var paused = await book.PauseAsync(created.RunId);
        Assert.Equal("Paused", paused.Phase);
        var resumed = await book.ResumeAsync(created.RunId);
        Assert.Equal("Running", resumed.Phase);
        await book.SetPlaybackAsync(created.RunId, 60);
        var reset = await book.ResetAsync(created.RunId, confirmDiscard: false);
        Assert.NotEqual(created.RunId, reset.RunId);
        Assert.Equal(2, reset.Generation);
        Assert.Equal(1, reset.Version);
        Assert.Equal(42, reset.Seed);
        Assert.Equal(1, reset.PlaybackRate);
        Assert.Equal("Ready", reset.Phase);
        Assert.Equal(0, reset.SimulatedSeconds);
        Assert.Null(book.SnapshotOf(created.RunId));
    }

    [Fact]
    public async Task Finished_uncommitted_run_requires_confirm_before_reset_or_new_run()
    {
        var (book, _, _) = NewBook();
        var created = await book.CreateAsync(_eventId, _distanceId, null, 7);
        await book.StartAsync(created.RunId);
        await book.AdvanceRunningAsync(60, CancellationToken.None);
        var finished = book.SnapshotOf(created.RunId);
        Assert.Equal("Finished", finished!.Phase);
        Assert.True(finished.DiscardRequired);
        await Assert.ThrowsAsync<DomainException>(() => book.ResetAsync(created.RunId, false));
        Assert.Equal(created.RunId, book.SnapshotOf(created.RunId)!.RunId);

        var replaced = await book.NewRunAsync(created.RunId, confirmDiscard: true, seed: 99, runnerCount: null);
        Assert.Equal(99, replaced.Seed);
        Assert.Equal(2, replaced.Generation);
        Assert.Equal("Ready", replaced.Phase);
    }

    [Fact]
    public async Task New_run_keeps_the_old_run_when_the_field_is_invalid()
    {
        var (book, fields, _) = NewBook();
        var created = await book.CreateAsync(_eventId, _distanceId, null, 7);
        fields.Next = BrokenField();
        await Assert.ThrowsAsync<DomainException>(() => book.NewRunAsync(created.RunId, true, 5, null));
        var still = book.SnapshotOf(created.RunId);
        Assert.NotNull(still);
        Assert.Equal(7, still.Seed);
        Assert.Equal(1, still.Generation);
    }

    [Fact]
    public async Task Requested_count_above_eligible_is_rejected()
    {
        var (book, _, _) = NewBook();
        var error = await Assert.ThrowsAsync<DomainException>(() => book.CreateAsync(_eventId, _distanceId, 9, 1));
        Assert.Contains("9", error.Message);
        Assert.Contains("2", error.Message);
    }

    [Fact]
    public async Task Commit_before_finish_is_rejected_and_does_not_call_the_port()
    {
        var (book, _, gateway) = NewBook();
        var created = await book.CreateAsync(_eventId, _distanceId, null, 1);
        await book.StartAsync(created.RunId);
        await Assert.ThrowsAsync<DomainException>(() => book.CommitAsync(created.RunId));
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task Commit_then_retry_partial_then_repeat_does_not_overlap()
    {
        var (book, _, gateway) = NewBook();
        var created = await Finish(book);
        gateway.Handler = command =>
        {
            var items = command.Drafts.Select((draft, index) => new SimulationCommitItemResult(
                draft.RegistrationId,
                index == 0 ? SimulationCommitOutcomes.CommittedThisRun : SimulationCommitOutcomes.Retryable,
                index == 0 ? null : "Chưa ghi được. Có thể thử lại.",
                index == 0 ? SimulationClockMath.RoundSeconds(draft.ElapsedSeconds) : null)).ToList();
            return Task.FromResult(new SimulationCommitBatch(true, null, items));
        };

        var first = await book.CommitAsync(created.RunId);
        Assert.Equal("Partial", first.Snapshot.CommitState);
        Assert.Contains(first.Items, item => item.Outcome == SimulationCommitOutcomes.Retryable);
        var pending = first.Items.Single(item => item.Outcome == SimulationCommitOutcomes.Retryable).RegistrationId;

        gateway.Handler = command =>
        {
            Assert.Single(command.Drafts);
            Assert.Equal(pending, command.Drafts[0].RegistrationId);
            return Task.FromResult(new SimulationCommitBatch(true, null,
            [
                new SimulationCommitItemResult(pending, SimulationCommitOutcomes.CommittedThisRun, null, 1)
            ]));
        };
        var second = await book.CommitAsync(created.RunId);
        Assert.Equal("Committed", second.Snapshot.CommitState);
        Assert.Equal(2, gateway.Calls);

        var calls = gateway.Calls;
        var repeat = await book.CommitAsync(created.RunId);
        Assert.Equal(calls, gateway.Calls);
        Assert.All(repeat.Items, item => Assert.Equal(SimulationCommitOutcomes.CommittedThisRun, item.Outcome));
    }

    [Fact]
    public async Task Two_commits_do_not_run_the_port_together()
    {
        var (book, _, gateway) = NewBook();
        var created = await Finish(book);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.Handler = async command =>
        {
            entered.TrySetResult();
            await release.Task;
            return new SimulationCommitBatch(true, null, command.Drafts.Select(draft =>
                new SimulationCommitItemResult(draft.RegistrationId, SimulationCommitOutcomes.CommittedThisRun, null, 1)).ToList());
        };

        var first = book.CommitAsync(created.RunId);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<SimulationBusyException>(() => book.ResetAsync(created.RunId, true));
        var second = book.CommitAsync(created.RunId);
        release.TrySetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(1, gateway.MaxConcurrency);
        Assert.Equal(1, gateway.Calls);
    }

    [Fact]
    public async Task Sos_and_dnf_stay_inside_the_session()
    {
        var (book, _, gateway) = NewBook();
        var created = await book.CreateAsync(_eventId, _distanceId, null, 3);
        await book.StartAsync(created.RunId);
        var sos = await book.SosAsync(created.RunId, _a);
        Assert.True(sos.Athletes.Single(athlete => athlete.RegistrationId == _a).Sos);
        var dnf = await book.DnfAsync(created.RunId, _b);
        Assert.Equal("Dnf", dnf.Athletes.Single(athlete => athlete.RegistrationId == _b).Motion);
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task Slider_rejects_speed_above_40_and_accepts_zero()
    {
        var (book, _, _) = NewBook();
        var created = await book.CreateAsync(_eventId, _distanceId, null, 3);
        await Assert.ThrowsAsync<DomainException>(() => book.SetBaseSpeedAsync(created.RunId, _a, 41));
        var updated = await book.SetBaseSpeedAsync(created.RunId, _a, 0);
        Assert.Equal(0, updated.Athletes.Single(athlete => athlete.RegistrationId == _a).BaseKmh);
    }

    [Fact]
    public async Task Read_requires_event_permission_and_returns_the_latest_version()
    {
        var (book, _, _) = NewBook();
        var created = await book.CreateAsync(_eventId, _distanceId, null, 3);
        var allowed = new SimulationAuthorizer(new FakeUser(Guid.NewGuid(), admin: true), new FakeAccess(allow: true));
        var denied = new SimulationAuthorizer(new FakeUser(Guid.NewGuid(), admin: true), new FakeAccess(allow: false));
        await Assert.ThrowsAsync<DomainException>(() => SimulationAccess.ReadAsync(book, denied, created.RunId));
        var started = await book.StartAsync(created.RunId);
        var read = await SimulationAccess.ReadAsync(book, allowed, created.RunId);
        Assert.Equal(started.Version, read.Version);
        Assert.Equal(started.RunId, read.RunId);
        Assert.Equal(started.Generation, read.Generation);
        var reread = await SimulationAccess.ReadAsync(book, allowed, created.RunId);
        Assert.Equal(read.Version, reread.Version);
    }

    [Fact]
    public async Task Preview_reports_the_active_run_without_creating_one()
    {
        var (book, _, _) = NewBook();
        var before = await book.PreviewAsync(_eventId, _distanceId);
        Assert.Null(before.ActiveRunId);
        Assert.Null(JsonActiveRun(before));

        var created = await book.CreateAsync(_eventId, _distanceId, null, 42);
        var during = await book.PreviewAsync(_eventId, _distanceId);
        Assert.Equal(created.RunId, during.ActiveRunId);
        Assert.Equal(created.RunId, JsonActiveRun(during));

        var otherDistance = Guid.Parse("33333333-3333-3333-3333-333333333333");
        Assert.Null((await book.PreviewAsync(_eventId, otherDistance)).ActiveRunId);

        await book.StartAsync(created.RunId);
        await book.AdvanceRunningAsync(60, CancellationToken.None);
        var finished = await book.PreviewAsync(_eventId, _distanceId);
        Assert.Equal(created.RunId, finished.ActiveRunId);
        Assert.Equal("Finished", book.SnapshotOf(created.RunId)!.Phase);

        var reset = await book.ResetAsync(created.RunId, confirmDiscard: true);
        Assert.Equal(reset.RunId, (await book.PreviewAsync(_eventId, _distanceId)).ActiveRunId);
        Assert.NotEqual(created.RunId, reset.RunId);

        var newer = await book.NewRunAsync(reset.RunId, confirmDiscard: false, seed: 99, runnerCount: null);
        Assert.Equal(newer.RunId, (await book.PreviewAsync(_eventId, _distanceId)).ActiveRunId);
        Assert.Null((await book.PreviewAsync(_eventId, otherDistance)).ActiveRunId);
    }

    [Fact]
    public async Task Commit_keeps_a_success_and_rereads_an_unknown_registration()
    {
        var (book, _, gateway) = NewBook();
        var created = await Finish(book);
        var seen = new List<SimulationCommitCommand>();
        gateway.Handler = command =>
        {
            seen.Add(command);
            if (seen.Count == 1)
            {
                return Task.FromResult(new SimulationCommitBatch(true, null,
                [
                    new SimulationCommitItemResult(_a, SimulationCommitOutcomes.CommittedThisRun, null, 1),
                    new SimulationCommitItemResult(
                        _b,
                        SimulationCommitOutcomes.Unknown,
                        "Không rõ đã ghi hay chưa. Hãy đọc lại đăng ký trước khi thử lại.",
                        null)
                ]));
            }

            Assert.DoesNotContain(command.Drafts, draft => draft.RegistrationId == _a);
            Assert.Contains(command.Drafts, draft => draft.RegistrationId == _b);
            Assert.Contains(_b, command.UnresolvedRegistrationIds ?? []);
            return Task.FromResult(new SimulationCommitBatch(true, null,
            [
                new SimulationCommitItemResult(
                    _b,
                    SimulationCommitOutcomes.Unknown,
                    "Đã có kết quả nhưng không xác định được nguồn ghi. Giữ nguyên dữ liệu.",
                    null)
            ]));
        };

        var first = await book.CommitAsync(created.RunId);
        Assert.Contains(first.Items, item => item.RegistrationId == _a && item.Outcome == SimulationCommitOutcomes.CommittedThisRun);
        Assert.Contains(first.Items, item => item.RegistrationId == _b && item.Outcome == SimulationCommitOutcomes.Unknown);

        var second = await book.CommitAsync(created.RunId);
        Assert.Equal(2, seen.Count);
        Assert.Contains(second.Items, item => item.RegistrationId == _a && item.Outcome == SimulationCommitOutcomes.CommittedThisRun);
        Assert.DoesNotContain(seen[1].Drafts, draft => draft.RegistrationId == _a);
    }

    [Fact]
    public async Task Two_commits_keep_snapshot_bib_A0042()
    {
        var (book, fields, gateway) = NewBook();
        var field = ValidField();
        fields.Next = new SimulationField
        {
            EventId = field.EventId,
            DistanceId = field.DistanceId,
            Title = field.Title,
            DistanceKm = field.DistanceKm,
            CourseError = field.CourseError,
            Polyline = field.Polyline,
            Checkpoints = field.Checkpoints,
            Eligible = [new SimulationAthleteSeed(_a, "A0042", "An", "chip-a")]
        };

        var bibs = new List<string>();
        gateway.Handler = command =>
        {
            bibs.AddRange(command.Drafts.Select(draft => draft.Bib));
            return Task.FromResult(new SimulationCommitBatch(
                true,
                null,
                command.Drafts.Select(draft => new SimulationCommitItemResult(
                    draft.RegistrationId,
                    SimulationCommitOutcomes.Skipped,
                    "Đăng ký đã đổi BIB sau khi kiểm tra. Không ghi với dữ liệu mới.",
                    null)).ToList()));
        };

        var created = await Finish(book);
        Assert.Equal("A0042", Assert.Single(book.SnapshotOf(created.RunId)!.Athletes).Bib);

        var first = await book.CommitAsync(created.RunId);
        var second = await book.CommitAsync(created.RunId);

        Assert.Equal(SimulationCommitOutcomes.Skipped, Assert.Single(first.Items).Outcome);
        Assert.Equal(SimulationCommitOutcomes.Skipped, Assert.Single(second.Items).Outcome);
        Assert.Equal(["A0042", "A0042"], bibs);
        Assert.DoesNotContain("Z9000", bibs);
    }

    [Fact]
    public async Task Broadcast_failure_on_commit_does_not_leave_the_run_committing()
    {
        var broadcaster = new GateBroadcaster();
        var (book, _, gateway) = NewBook(broadcaster);
        var created = await Finish(book);
        broadcaster.CommitStates.Clear();
        broadcaster.FailNext = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => book.CommitAsync(created.RunId));
        var snapshot = book.SnapshotOf(created.RunId);
        Assert.NotNull(snapshot);
        Assert.NotEqual("Committing", snapshot.CommitState);
        Assert.Equal(0, gateway.Calls);
        Assert.Equal("Idle", Assert.Single(broadcaster.CommitStates));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var second = await book.CommitAsync(created.RunId, timeout.Token);
        Assert.NotEqual("Committing", second.Snapshot.CommitState);
        Assert.Equal(1, gateway.Calls);
    }

    private async Task<SimulationSnapshotDto> Finish(SimulationSessionBook book)
    {
        var created = await book.CreateAsync(_eventId, _distanceId, null, 4);
        await book.StartAsync(created.RunId);
        await book.AdvanceRunningAsync(60, CancellationToken.None);
        var finished = book.SnapshotOf(created.RunId);
        Assert.Equal("Finished", finished!.Phase);
        return finished;
    }

    private static Guid? JsonActiveRun(SimulationPreviewDto preview)
    {
        var json = JsonSerializer.Serialize(preview, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var value = document.RootElement.GetProperty("activeRunId");
        return value.ValueKind == JsonValueKind.Null ? null : value.GetGuid();
    }

    private (SimulationSessionBook Book, MutableFields Fields, RecordingGateway Gateway) NewBook(
        ISimulationBroadcaster? broadcaster = null)
    {
        var fields = new MutableFields(ValidField());
        var gateway = new RecordingGateway();
        var book = new SimulationSessionBook(fields, gateway, broadcaster ?? new NoopSimulationBroadcaster());
        return (book, fields, gateway);
    }

    private SimulationField ValidField()
        => new()
        {
            EventId = _eventId,
            DistanceId = _distanceId,
            Title = "Demo",
            DistanceKm = 0.05,
            CourseError = null,
            Polyline = [new SimulationCoursePoint(10, 106), new SimulationCoursePoint(10.1, 106.1)],
            Checkpoints =
            [
                new SimulationCourseCheckpoint("start", "Start", 0, 10, 106, "start"),
                new SimulationCourseCheckpoint("finish", "Finish", 0.05, 10.1, 106.1, "finish")
            ],
            Eligible =
            [
                new SimulationAthleteSeed(_a, "A001", "An", "chip-a"),
                new SimulationAthleteSeed(_b, "A002", "Ba", "chip-b")
            ]
        };

    private SimulationField BrokenField()
    {
        var field = ValidField();
        return new SimulationField
        {
            EventId = field.EventId,
            DistanceId = field.DistanceId,
            Title = field.Title,
            DistanceKm = field.DistanceKm,
            CourseError = "Chưa có course cho cự ly này.",
            Polyline = field.Polyline,
            Checkpoints = field.Checkpoints,
            Eligible = field.Eligible
        };
    }

    private sealed class MutableFields(SimulationField next) : ISimulationFieldSource
    {
        public SimulationField Next { get; set; } = next;

        public Task<SimulationField> LoadAsync(Guid eventId, Guid distanceId, CancellationToken cancellationToken = default)
            => Task.FromResult(Next);
    }

    private sealed class RecordingGateway : ISimulationCommitGateway
    {
        private int _current;

        public int Calls { get; private set; }
        public int MaxConcurrency { get; private set; }
        public Func<SimulationCommitCommand, Task<SimulationCommitBatch>>? Handler { get; set; }

        public async Task<SimulationCommitBatch> CommitAsync(
            SimulationCommitCommand command,
            CancellationToken cancellationToken = default)
        {
            var now = Interlocked.Increment(ref _current);
            MaxConcurrency = Math.Max(MaxConcurrency, now);
            Calls++;
            try
            {
                if (Handler is not null)
                {
                    return await Handler(command);
                }

                return new SimulationCommitBatch(true, null, command.Drafts.Select(draft =>
                    new SimulationCommitItemResult(
                        draft.RegistrationId,
                        SimulationCommitOutcomes.CommittedThisRun,
                        null,
                        SimulationClockMath.RoundSeconds(draft.ElapsedSeconds))).ToList());
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }
    }

    private sealed class GateBroadcaster : ISimulationBroadcaster
    {
        public bool FailNext { get; set; }
        public List<string> CommitStates { get; } = [];

        public Task PublishAsync(SimulationSnapshotDto snapshot, CancellationToken cancellationToken = default)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("hub");
            }

            CommitStates.Add(snapshot.CommitState);
            return Task.CompletedTask;
        }

        public Task PublishReplacedAsync(SimulationRunReplacedDto notice, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeUser(Guid id, bool admin) : ICurrentUserAccessor
    {
        public Guid? UserId => id;
        public bool IsInRole(string role) => admin && role == "Admin";
    }

    private sealed class FakeAccess(bool allow) : IEventSosOpsAccessQuery
    {
        public Task<bool> CanManageSosAsync(Guid eventId, Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(allow);
    }
}
