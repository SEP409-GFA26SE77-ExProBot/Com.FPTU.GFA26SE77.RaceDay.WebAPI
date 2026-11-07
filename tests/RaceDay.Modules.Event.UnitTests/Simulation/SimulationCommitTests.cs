using Microsoft.Extensions.Logging;
using RaceDay.Modules.Event.Application.Simulation;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Contracts.Simulation;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.UnitTests.Simulation;

public class SimulationCommitTests
{
    private readonly Guid _eventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _distanceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly Guid _registrationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private readonly DateTime _start = new(2026, 11, 7, 6, 30, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0.5, 1)]
    [InlineData(1.5, 2)]
    [InlineData(2.5, 3)]
    [InlineData(90.4, 90)]
    [InlineData(90.5, 91)]
    public void Result_seconds_round_away_from_zero(double elapsed, long expected)
    {
        Assert.Equal(expected, ResultTime.RoundElapsed(elapsed));
        var finish = DateTime.SpecifyKind(_start.AddSeconds(expected), DateTimeKind.Utc);
        Assert.Equal(expected, ResultTime.RoundBetween(_start, finish));
    }

    [Fact]
    public void Domain_finish_does_not_set_started_at()
    {
        var registration = EventRegistration.Create(
            _eventId, _distanceId, Guid.NewGuid(),
            "Nguyen Van A", "a@raceday.vn", "0901111222",
            new DateOnly(1995, 1, 1), "Male", null, null, null, null,
            null, null, null, null, null, "[]", null, null, true);
        registration.MarkPaidPendingApproval();
        registration.Confirm("A0042");
        Assert.Null(registration.StartedAtUtc);

        Assert.True(registration.Finish(_start.AddSeconds(91), 91));
        Assert.Null(registration.StartedAtUtc);
        Assert.Equal(91, registration.ResultSeconds);
        Assert.Equal(RegistrationStatus.Finished, registration.Status);
        Assert.Equal(RaceStatus.Finished, registration.RaceStatus);
        Assert.False(registration.Finish(_start.AddSeconds(10), 10));
        Assert.Equal(91, registration.ResultSeconds);
    }

    [Fact]
    public void Official_start_uses_distance_clock_only_when_the_zone_is_set()
    {
        var clocks = new OfficialStartClocks(new DateOnly(2026, 11, 7), new TimeOnly(6, 30, 0), new TimeOnly(7, 0, 0));
        Assert.False(SimulationOfficialStartResolver.TryResolve(false, "UTC", clocks, out _, out var disabled));
        Assert.Equal("Giả lập đang tắt.", disabled);

        Assert.False(SimulationOfficialStartResolver.TryResolve(true, "  ", clocks, out _, out var missing));
        Assert.Equal(SimulationOfficialStartResolver.UnresolvedReason, missing);

        Assert.False(SimulationOfficialStartResolver.TryResolve(true, "Not/AZone", clocks, out _, out var invalid));
        Assert.Contains("StartTimeZone", invalid);

        Assert.False(SimulationOfficialStartResolver.TryResolve(
            true, "UTC", new OfficialStartClocks(new DateOnly(2026, 11, 7), null, null), out _, out var noClock));
        Assert.Contains("giờ xuất phát", noClock);

        Assert.True(SimulationOfficialStartResolver.TryResolve(true, "UTC", clocks, out var start, out _));
        Assert.Equal(new DateTime(2026, 11, 7, 6, 30, 0, DateTimeKind.Utc), start);

        var shifted = TimeZoneInfo.GetSystemTimeZones()
            .FirstOrDefault(zone => zone.BaseUtcOffset == TimeSpan.FromHours(7) && !zone.SupportsDaylightSavingTime);
        Assert.NotNull(shifted);
        Assert.True(SimulationOfficialStartResolver.TryResolve(true, shifted!.Id, clocks, out var local, out _));
        Assert.Equal(new DateTime(2026, 11, 6, 23, 30, 0, DateTimeKind.Utc), local);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("1", false)]
    [InlineData(null, false)]
    public void Feature_flag_accepts_only_boolean_text(string? value, bool expected)
        => Assert.Equal(expected, SimulationFeatureFlag.IsEnabled(value));

    [Fact]
    public async Task Flag_off_or_missing_zone_writes_nothing()
    {
        var writer = new FakeWriter();
        var off = Committer(writer, enabled: false, zone: "UTC");
        var refused = await off.CommitAsync(Command(10));
        Assert.False(refused.Accepted);
        Assert.Equal(0, writer.Calls);

        var noZone = Committer(writer, enabled: true, zone: null);
        var blocked = await noZone.CommitAsync(Command(90.5));
        Assert.False(blocked.Accepted);
        Assert.Equal(SimulationOfficialStartResolver.UnresolvedReason, blocked.RefusalReason);
        Assert.Equal(0, writer.Calls);
    }

    [Fact]
    public async Task Permission_and_ineligible_rows_do_not_write()
    {
        var writer = new FakeWriter();
        var denied = Committer(writer, enabled: true, zone: "UTC", allow: false, admin: true);
        await Assert.ThrowsAsync<DomainException>(() => denied.CommitAsync(Command(10)));
        Assert.Equal(0, writer.Calls);

        var committer = Committer(writer, enabled: true, zone: "UTC");
        var already = new HashSet<Guid> { _registrationId };
        var repeated = await committer.CommitAsync(new SimulationCommitCommand(
            _eventId, _distanceId, [new SimulationFinishDraft(_registrationId, 10, "A0042")], already));
        Assert.Equal(SimulationCommitOutcomes.CommittedThisRun, repeated.Items[0].Outcome);
        Assert.Single(already);
        Assert.Equal(0, writer.Calls);

        foreach (var row in new[]
                 {
                     Row(status: "Cancelled"),
                     Row(race: "DNS"),
                     Row(race: "DNF"),
                     Row(race: "DQ"),
                     Row(started: _start),
                     Row(status: "Finished", race: "Finished", result: 10),
                     null
                 })
        {
            writer.Calls = 0;
            var query = new FakeQuery(row);
            var item = (await Committer(writer, query: query).CommitAsync(Command(10))).Items.Single();
            Assert.NotEqual(SimulationCommitOutcomes.CommittedThisRun, item.Outcome);
            Assert.Equal(0, writer.Calls);
        }
    }

    [Fact]
    public async Task Eligible_finish_stores_rounded_seconds_and_classifies_zero_rows()
    {
        var writer = new FakeWriter { Rows = 1 };
        var query = new FakeQuery(Row());
        var batch = await Committer(writer, query: query).CommitAsync(Command(90.5));
        Assert.True(batch.Accepted);
        Assert.Equal(SimulationCommitOutcomes.CommittedThisRun, batch.Items[0].Outcome);
        Assert.Equal(91, batch.Items[0].ResultSeconds);
        Assert.Equal(91, writer.LastSeconds);
        Assert.Equal(DateTimeKind.Utc, writer.LastFinish!.Value.Kind);
        Assert.Equal(_eventId, writer.LastEventId);
        Assert.Equal(_distanceId, writer.LastDistanceId);
        Assert.Equal("A0042", writer.LastBib);
        Assert.Null(writer.StartedAtWasSet);

        writer.Rows = 0;
        query.Next = Row();
        var retry = await Committer(writer, query: query).CommitAsync(Command(10));
        Assert.Equal(SimulationCommitOutcomes.Retryable, retry.Items[0].Outcome);

        writer.Rows = 0;
        query.Next = Row(status: "Finished", race: "Finished", result: 91);
        var elsewhere = await Committer(writer, query: query).CommitAsync(Command(10));
        Assert.Equal(SimulationCommitOutcomes.AlreadyRecordedElsewhere, elsewhere.Items[0].Outcome);
    }

    [Fact]
    public async Task Writer_failures_are_skipped_or_unknown_without_leaking_the_exception()
    {
        var logger = new CapturingLogger();
        var writer = new FakeWriter { Error = new DomainException("Chỉ VĐV confirmed mới finish được.") };
        var skipped = await Committer(writer, logger: logger).CommitAsync(Command(10));
        Assert.Equal(SimulationCommitOutcomes.Skipped, skipped.Items[0].Outcome);
        Assert.Equal("Chỉ VĐV confirmed mới finish được.", skipped.Items[0].Reason);

        writer.Error = new InvalidOperationException("db connection 10.1.2.3");
        var unknown = await Committer(writer, logger: logger).CommitAsync(Command(10));
        Assert.Equal(SimulationCommitOutcomes.Unknown, unknown.Items[0].Outcome);
        Assert.Equal("Không rõ đã ghi hay chưa. Hãy đọc lại đăng ký trước khi thử lại.", unknown.Items[0].Reason);
        Assert.DoesNotContain("10.1.2.3", unknown.Items[0].Reason);
        Assert.IsType<InvalidOperationException>(Assert.Single(logger.Errors));

        writer.Error = new OperationCanceledException();
        var cancelled = await Committer(writer, logger: logger).CommitAsync(Command(10));
        Assert.Equal(SimulationCommitOutcomes.Unknown, cancelled.Items[0].Outcome);
    }

    [Fact]
    public async Task Changed_distance_or_bib_is_skipped_without_writing_the_new_values()
    {
        var other = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");
        var movedDistance = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var writer = new FakeWriter { Rows = 0 };
        var query = new FakeQuery(Row());
        query.Rows[other] = Row(other);
        query.Sequences[_registrationId] = new Queue<SimulationRegistrationState?>(
        [
            Row(),
            Row() with { DistanceId = movedDistance }
        ]);
        query.Sequences[other] = new Queue<SimulationRegistrationState?>(
        [
            Row(other),
            Row(other) with { Bib = "Z9000" }
        ]);

        var batch = await Committer(writer, query: query).CommitAsync(new SimulationCommitCommand(
            _eventId,
            _distanceId,
            [new SimulationFinishDraft(_registrationId, 10, "A0042"), new SimulationFinishDraft(other, 12, "A0042")],
            []));

        Assert.Equal(2, writer.Calls);
        Assert.Equal([_registrationId, other], writer.AttemptIds);
        Assert.All(writer.EventIds, id => Assert.Equal(_eventId, id));
        Assert.All(writer.DistanceIds, id => Assert.Equal(_distanceId, id));
        Assert.All(writer.Bibs, bib => Assert.Equal("A0042", bib));
        Assert.DoesNotContain(movedDistance, writer.DistanceIds);
        Assert.DoesNotContain("Z9000", writer.Bibs);
        Assert.Equal(SimulationCommitOutcomes.Skipped, batch.Items[0].Outcome);
        Assert.Equal(SimulationResultCommitter.ChangedDistanceOrEventReason, batch.Items[0].Reason);
        Assert.Equal(SimulationCommitOutcomes.Skipped, batch.Items[1].Outcome);
        Assert.Equal(SimulationResultCommitter.ChangedBibReason, batch.Items[1].Reason);
    }

    [Fact]
    public async Task Snapshot_bib_A0042_is_not_written_after_the_registration_changes_to_Z9000()
    {
        var writer = new FakeWriter { Rows = 1 };
        var query = new FakeQuery(Row() with { Bib = "Z9000" });
        var committer = Committer(writer, query: query);
        var command = new SimulationCommitCommand(
            _eventId,
            _distanceId,
            [new SimulationFinishDraft(_registrationId, 10, "A0042")],
            []);

        var first = await committer.CommitAsync(command);
        var second = await committer.CommitAsync(command);

        Assert.Equal(SimulationCommitOutcomes.Skipped, first.Items.Single().Outcome);
        Assert.Equal(SimulationResultCommitter.ChangedBibReason, first.Items.Single().Reason);
        Assert.Equal(SimulationCommitOutcomes.Skipped, second.Items.Single().Outcome);
        Assert.Equal(SimulationResultCommitter.ChangedBibReason, second.Items.Single().Reason);
        Assert.Equal(0, writer.Calls);
        Assert.DoesNotContain("Z9000", writer.Bibs);
    }

    [Fact]
    public async Task Read_failure_keeps_the_written_registration_off_the_next_writer_call()
    {
        var other = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");
        var writer = new FakeWriter();
        var logger = new CapturingLogger();
        var query = new FakeQuery(Row());
        query.Rows[other] = Row(other);
        query.FailReads.Add(other);
        query.ReadError = new InvalidOperationException("connection reset to 10.9.8.7");
        var committer = Committer(writer, query: query, logger: logger);

        var first = await committer.CommitAsync(new SimulationCommitCommand(
            _eventId,
            _distanceId,
            [new SimulationFinishDraft(_registrationId, 10, "A0042"), new SimulationFinishDraft(other, 12, "A0042")],
            []));

        Assert.Equal(SimulationCommitOutcomes.CommittedThisRun, first.Items[0].Outcome);
        Assert.Equal(_registrationId, first.Items[0].RegistrationId);
        Assert.Equal(SimulationCommitOutcomes.Retryable, first.Items[1].Outcome);
        Assert.Equal("Chưa ghi được. Có thể thử lại.", first.Items[1].Reason);
        Assert.DoesNotContain("10.9.8.7", first.Items[1].Reason);
        Assert.Equal([_registrationId], writer.AttemptIds);
        Assert.IsType<InvalidOperationException>(Assert.Single(logger.Errors));

        query.ReadError = null;
        var committed = first.Items
            .Where(item => item.Outcome == SimulationCommitOutcomes.CommittedThisRun)
            .Select(item => item.RegistrationId)
            .ToList();
        var second = await committer.CommitAsync(new SimulationCommitCommand(
            _eventId,
            _distanceId,
            [new SimulationFinishDraft(other, 12, "A0042")],
            committed));

        Assert.Equal([_registrationId, other], writer.AttemptIds);
        Assert.Equal(SimulationCommitOutcomes.CommittedThisRun, second.Items.Single().Outcome);
        Assert.Equal(other, second.Items.Single().RegistrationId);
    }

    [Fact]
    public async Task Unknown_write_is_reread_and_not_claimed_when_a_result_exists()
    {
        var writer = new FakeWriter { Error = new InvalidOperationException("timeout while committing") };
        var logger = new CapturingLogger();
        var query = new FakeQuery(Row());
        var committer = Committer(writer, query: query, logger: logger);

        var first = await committer.CommitAsync(Command(10));
        Assert.Equal(SimulationCommitOutcomes.Unknown, first.Items[0].Outcome);
        Assert.DoesNotContain("timeout", first.Items[0].Reason ?? "");
        Assert.NotEmpty(logger.Errors);

        writer.Error = null;
        writer.Calls = 0;
        writer.AttemptIds.Clear();
        query.Rows[_registrationId] = Row(status: "Finished", race: "Finished", result: 91);
        var second = await committer.CommitAsync(new SimulationCommitCommand(
            _eventId,
            _distanceId,
            [new SimulationFinishDraft(_registrationId, 10, "A0042")],
            [],
            [_registrationId]));

        Assert.Equal(SimulationCommitOutcomes.Unknown, second.Items[0].Outcome);
        Assert.NotEqual(SimulationCommitOutcomes.CommittedThisRun, second.Items[0].Outcome);
        Assert.NotEqual(SimulationCommitOutcomes.AlreadyRecordedElsewhere, second.Items[0].Outcome);
        Assert.Equal(SimulationResultCommitter.ResultSourceUnknownReason, second.Items[0].Reason);
        Assert.Equal(0, writer.Calls);
        Assert.Empty(writer.AttemptIds);
    }

    [Fact]
    public async Task Unknown_stays_unknown_when_the_next_call_is_cancelled_before_reread()
    {
        var writer = new FakeWriter { Error = new InvalidOperationException("timeout while committing") };
        var query = new FakeQuery(Row());
        var committer = Committer(writer, query: query);

        var first = await committer.CommitAsync(Command(10));
        Assert.Equal(SimulationCommitOutcomes.Unknown, first.Items[0].Outcome);
        var readsAfterWrite = query.Reads;
        var writesAfterFirst = writer.Calls;

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var second = await committer.CommitAsync(
            new SimulationCommitCommand(
                _eventId,
                _distanceId,
                [new SimulationFinishDraft(_registrationId, 10, "A0042")],
                [],
                [_registrationId]),
            cancelled.Token);

        Assert.Equal(SimulationCommitOutcomes.Unknown, second.Items.Single().Outcome);
        Assert.NotEqual(SimulationCommitOutcomes.Retryable, second.Items.Single().Outcome);
        Assert.Equal(readsAfterWrite, query.Reads);
        Assert.Equal(writesAfterFirst, writer.Calls);

        query.Rows[_registrationId] = Row(status: "Finished", race: "Finished", result: 10);
        var third = await committer.CommitAsync(new SimulationCommitCommand(
            _eventId,
            _distanceId,
            [new SimulationFinishDraft(_registrationId, 10, "A0042")],
            [],
            [_registrationId]));

        Assert.Equal(SimulationCommitOutcomes.Unknown, third.Items.Single().Outcome);
        Assert.Equal(SimulationResultCommitter.ResultSourceUnknownReason, third.Items.Single().Reason);
        Assert.NotEqual(SimulationCommitOutcomes.CommittedThisRun, third.Items.Single().Outcome);
        Assert.NotEqual(SimulationCommitOutcomes.AlreadyRecordedElsewhere, third.Items.Single().Outcome);
        Assert.Equal(writesAfterFirst, writer.Calls);
        Assert.DoesNotContain(writer.AttemptIds.Skip(writesAfterFirst), id => id == _registrationId);
    }

    private SimulationResultCommitter Committer(
        FakeWriter writer,
        bool enabled = true,
        string? zone = "UTC",
        bool allow = true,
        bool admin = false,
        FakeQuery? query = null,
        CapturingLogger? logger = null)
        => new(
            new FakeGate(enabled, zone),
            new FakeStart(_start),
            query ?? new FakeQuery(Row()),
            writer,
            new FakeUser(Guid.NewGuid(), admin),
            new FakeAccess(allow),
            logger ?? new CapturingLogger());

    private SimulationCommitCommand Command(double elapsed)
        => new(_eventId, _distanceId, [new SimulationFinishDraft(_registrationId, elapsed, "A0042")], []);

    private SimulationRegistrationState Row(
        Guid? id = null,
        string status = "Confirmed",
        string race = "NotStarted",
        DateTime? started = null,
        long? result = null)
        => new(id ?? _registrationId, _eventId, _distanceId, "A0042", "An", status, race, started, result is null ? null : _start, result);

    private sealed class FakeGate(bool enabled, string? zone) : ISimulationDemoGate
    {
        public bool Enabled => enabled;
        public string? StartTimeZone => zone;
    }

    private sealed class FakeStart(DateTime start) : ISimulationOfficialStartSource
    {
        public Task<OfficialStartClocks?> GetClocksAsync(Guid eventId, Guid distanceId, CancellationToken cancellationToken = default)
            => Task.FromResult<OfficialStartClocks?>(new OfficialStartClocks(
                DateOnly.FromDateTime(start), TimeOnly.FromDateTime(start), null));
    }

    private sealed class FakeQuery : ISimulationCandidateQuery
    {
        public FakeQuery(SimulationRegistrationState? row)
        {
            if (row is not null)
            {
                Rows[row.RegistrationId] = row;
            }
        }

        public Dictionary<Guid, SimulationRegistrationState?> Rows { get; } = new();
        public SimulationRegistrationState? Next { get; set; }
        public HashSet<Guid> FailReads { get; } = [];
        public Exception? ReadError { get; set; }
        public Dictionary<Guid, Queue<SimulationRegistrationState?>> Sequences { get; } = new();
        public int Reads { get; private set; }

        public Task<IReadOnlyList<SimulationRegistrationState>> ListEligibleAsync(
            Guid eventId, Guid distanceId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SimulationRegistrationState>>([]);

        public Task<SimulationRegistrationState?> GetAsync(Guid registrationId, CancellationToken cancellationToken = default)
        {
            Reads++;
            if (ReadError is not null && FailReads.Contains(registrationId))
            {
                throw ReadError;
            }

            if (Sequences.TryGetValue(registrationId, out var scripted) && scripted.Count > 0)
            {
                return Task.FromResult(scripted.Dequeue());
            }

            if (Next is not null || (Rows.TryGetValue(registrationId, out var current) && current is null))
            {
                var value = Next;
                Next = null;
                if (value is not null || !Rows.ContainsKey(registrationId))
                {
                    return Task.FromResult(value);
                }
            }

            Rows.TryGetValue(registrationId, out var row);
            return Task.FromResult(row);
        }
    }

    private sealed class FakeWriter : ISimulationFinishWriter
    {
        public int Calls { get; set; }
        public int Rows { get; set; } = 1;
        public Exception? Error { get; set; }
        public List<Guid> AttemptIds { get; } = [];
        public List<Guid> EventIds { get; } = [];
        public List<Guid> DistanceIds { get; } = [];
        public List<string> Bibs { get; } = [];
        public long? LastSeconds { get; private set; }
        public DateTime? LastFinish { get; private set; }
        public Guid? LastEventId { get; private set; }
        public Guid? LastDistanceId { get; private set; }
        public string? LastBib { get; private set; }
        public bool? StartedAtWasSet => null;

        public Task<int> WriteAsync(
            Guid registrationId,
            Guid eventId,
            Guid distanceId,
            string bib,
            DateTime finishUtc,
            long resultSeconds,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            AttemptIds.Add(registrationId);
            EventIds.Add(eventId);
            DistanceIds.Add(distanceId);
            Bibs.Add(bib);
            LastEventId = eventId;
            LastDistanceId = distanceId;
            LastBib = bib;
            if (Error is not null)
            {
                throw Error;
            }

            LastFinish = finishUtc;
            LastSeconds = resultSeconds;
            return Task.FromResult(Rows);
        }
    }

    private sealed class CapturingLogger : ILogger<SimulationResultCommitter>
    {
        public List<Exception> Errors { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => Empty.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (exception is not null)
            {
                Errors.Add(exception);
            }
        }

        private sealed class Empty : IDisposable
        {
            public static readonly Empty Instance = new();
            public void Dispose()
            {
            }
        }
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
