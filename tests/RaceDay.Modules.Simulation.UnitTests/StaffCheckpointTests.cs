using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Contracts.Simulation;
using RaceDay.Modules.Map.Contracts.Courses;
using RaceDay.Modules.Simulation.Application;
using RaceDay.Modules.Simulation.Contracts;
using RaceDay.Modules.Simulation.Domain;
using RaceDay.Modules.Simulation.Infrastructure;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Simulation.UnitTests;

public class StaffCheckpointTests
{
    private readonly Guid _eventId = Guid.NewGuid();
    private readonly Guid _distanceId = Guid.NewGuid();
    private readonly Guid _a = Guid.NewGuid();
    private readonly Guid _b = Guid.NewGuid();
    private readonly Guid _staff = Guid.NewGuid();

    [Fact]
    public async Task Dns_is_start_only_and_cannot_be_applied_after_start()
    {
        var (book, _) = CreateBook();
        var run = await book.CreateAsync(_eventId, _distanceId, null, 4);
        await Assert.ThrowsAsync<DomainException>(() => book.RecordDecisionAsync(run.RunId, _a, "DNS", "Absent", null, _staff, "cp"));
        var dns = await book.RecordDecisionAsync(run.RunId, _a, "DNS", "Absent", null, _staff, "start");
        Assert.Equal("Dns", dns.Athletes.Single(a => a.RegistrationId == _a).Motion);
        Assert.Equal("Ready", dns.Phase);
        var injected = await book.InjectPassageAsync(run.RunId, _a, "start");
        Assert.Empty(injected.Athletes.Single(a => a.RegistrationId == _a).Splits);
        var started = await book.StartAsync(run.RunId);
        Assert.Empty(started.Athletes.Single(a => a.RegistrationId == _a).Splits);
        await Assert.ThrowsAsync<DomainException>(() => book.RecordDecisionAsync(run.RunId, _b, "DNS", "Late", null, _staff, "start"));
    }

    [Fact]
    public async Task Dnf_requires_running_local_runner_and_a_reason()
    {
        var (book, _) = CreateBook();
        var run = await book.CreateAsync(_eventId, _distanceId, null, 4);
        await Assert.ThrowsAsync<DomainException>(() => book.RecordDecisionAsync(run.RunId, _a, "DNF", "Injury", null, _staff, "start"));
        await book.StartAsync(run.RunId);
        await Assert.ThrowsAsync<DomainException>(() => book.RecordDecisionAsync(run.RunId, _a, "DNF", "Injury", null, _staff, "finish"));
        await Assert.ThrowsAsync<DomainException>(() => book.RecordDecisionAsync(run.RunId, _a, "DNF", " ", null, _staff, "start"));
        var result = await book.RecordDecisionAsync(run.RunId, _a, "DNF", "Injury", null, _staff, "start");
        Assert.Equal("Dnf", result.Athletes.Single(a => a.RegistrationId == _a).Motion);
        var decision = Assert.Single(result.Decisions!);
        Assert.Equal(_staff, decision.ActorId);
        Assert.Equal("start", decision.CheckpointId);
        Assert.Equal("Racing", decision.PreviousStatus);
        var injected = await book.InjectPassageAsync(run.RunId, _a, "cp");
        Assert.DoesNotContain(injected.Athletes.Single(a => a.RegistrationId == _a).Splits, s => s.CheckpointId == "cp");
        await Assert.ThrowsAsync<DomainException>(() => book.RecordDecisionAsync(run.RunId, _a, "DQ", "Other", null, _staff, "start"));
    }

    [Fact]
    public async Task Pending_report_blocks_commit_and_approved_dq_is_not_ranked()
    {
        var (book, gateway) = CreateBook();
        var run = await book.CreateAsync(_eventId, _distanceId, null, 4);
        var reported = await book.RecordDecisionAsync(run.RunId, _a, "DQ", "Wrong bib", "Photo reference", _staff, "start");
        Assert.Equal("Racing", reported.Athletes.Single(a => a.RegistrationId == _a).Motion);
        await book.StartAsync(run.RunId);
        await book.AdvanceRunningAsync(200, default);
        Assert.Equal("Finished", book.SnapshotOf(run.RunId)!.Phase);
        await Assert.ThrowsAsync<DomainException>(() => book.CommitAsync(run.RunId));
        var reviewer = Guid.NewGuid();
        var reviewed = await book.ReviewDecisionAsync(run.RunId, reported.Decisions![0].Id, true, "Confirmed", reviewer);
        Assert.DoesNotContain(reviewed.Ranking, r => r.RegistrationId == _a);
        Assert.Equal("Dq", reviewed.Athletes.Single(a => a.RegistrationId == _a).Motion);
        var committed = await book.CommitAsync(run.RunId);
        Assert.Equal("Committed", committed.Snapshot.CommitState);
        var draft = gateway.Last!.Drafts.Single(x => x.RegistrationId == _a);
        Assert.Equal("DQ", draft.Decision!.Status);
        Assert.Equal(reviewer, draft.Decision.ActorId);
        Assert.Equal(run.RunId, draft.Decision.RunId);
        Assert.Equal(2, committed.Items.Count);
        var repeated = await book.CommitAsync(run.RunId);
        Assert.Null(repeated.Items.Single(x => x.RegistrationId == _a).ResultSeconds);
        await Assert.ThrowsAsync<DomainException>(() => book.RecordDecisionAsync(run.RunId, _b, "DQ", "Late", null, reviewer, "finish", true));
    }

    [Fact]
    public async Task Rejected_report_preserves_finish_and_terminal_decisions_commit_together()
    {
        var (book, gateway) = CreateBook();
        var run = await book.CreateAsync(_eventId, _distanceId, null, 4);
        var report = await book.RecordDecisionAsync(run.RunId, _a, "DQ", "Review", null, _staff, "start");
        await book.ReviewDecisionAsync(run.RunId, report.Decisions![0].Id, false, "No violation", Guid.NewGuid());
        await book.RecordDecisionAsync(run.RunId, _a, "DNS", "Absent", null, _staff, "start");
        await book.StartAsync(run.RunId);
        var finished = await book.RecordDecisionAsync(run.RunId, _b, "DNF", "Stopped", null, _staff, "start");
        Assert.Equal("Finished", finished.Phase);
        Assert.True(finished.DiscardRequired);
        Assert.Empty(finished.Ranking);
        await book.CommitAsync(run.RunId);
        Assert.Equal(new[] { "DNF", "DNS" }, gateway.Last!.Drafts.Select(x => x.Decision!.Status).Order().ToArray());
    }

    [Fact]
    public async Task Workspace_resolves_assignment_and_revocation_and_replacement()
    {
        var (book, _) = CreateBook();
        var assignments = new Assignments(new(Guid.NewGuid(), _eventId, "Race", $"map:{_distanceId}:cp"));
        var workspace = new StaffCheckpointWorkspace(assignments, new Courses(Field()), book);
        var waiting = await workspace.ReadAsync(_eventId, _staff);
        Assert.Null(waiting.RunId);
        var run = await book.CreateAsync(_eventId, _distanceId, null, 4);
        var current = await workspace.ReadAsync(_eventId, _staff);
        Assert.Equal(run.RunId, current.RunId);
        Assert.All(current.Athletes, a => Assert.False(a.CanDns));
        var replacement = await book.ResetAsync(run.RunId, true);
        Assert.Equal(replacement.RunId, (await workspace.ReadAsync(_eventId, _staff)).RunId);
        assignments.Member = assignments.Member! with { StationId = $"map:{_distanceId}:missing" };
        await Assert.ThrowsAsync<DomainException>(() => workspace.ReadAsync(_eventId, _staff));
        assignments.Member = null;
        await Assert.ThrowsAsync<DomainException>(() => workspace.ReadAsync(_eventId, _staff));
    }

    [Fact]
    public async Task Snapshot_counts_passage_by_split_and_hides_other_checkpoint_reports()
    {
        var (book, _) = CreateBook();
        var run = await book.CreateAsync(_eventId, _distanceId, null, 4);
        await book.RecordDecisionAsync(run.RunId, _a, "DQ", "Review", null, _staff, "start");
        await book.StartAsync(run.RunId);
        var workspace = new StaffCheckpointWorkspace(new Assignments(new(Guid.NewGuid(), _eventId, "Race", $"map:{_distanceId}:cp")), new Courses(Field()), book);
        var cp = await workspace.ReadAsync(_eventId, _staff);
        Assert.Empty(cp.Decisions);
        Assert.All(cp.Athletes, a => Assert.Null(a.PassedAtSeconds));
        await book.AdvanceRunningAsync(200);
        cp = await workspace.ReadAsync(_eventId, _staff);
        Assert.All(cp.Athletes, a => Assert.NotNull(a.PassedAtSeconds));
    }

    [Fact]
    public async Task Checkpoint_export_requires_finished_run_and_captures_all_runner_passages()
    {
        var (book, _) = CreateBook();
        var run = await book.CreateAsync(_eventId, _distanceId, null, 4);
        Assert.Throws<DomainException>(() => book.BuildCheckpointExport(run.RunId, "cp", _staff));
        await book.StartAsync(run.RunId);
        await book.AdvanceRunningAsync(200);

        var export = book.BuildCheckpointExport(run.RunId, "cp", _staff);
        Assert.Equal(run.RunId, export.RunId);
        Assert.Equal(_staff, export.StaffUserId);
        Assert.Equal("cp", export.CheckpointId);
        Assert.Equal(2, export.Rows.Count);
        Assert.All(export.Rows, row => Assert.NotNull(row.PassedAtSeconds));

        var reset = await book.ResetAsync(run.RunId, true);
        Assert.NotEqual(run.RunId, reset.RunId);
        Assert.Throws<DomainException>(() => book.BuildCheckpointExport(run.RunId, "cp", _staff));
    }

    private (SimulationSessionBook, Gateway) CreateBook()
    {
        var gateway = new Gateway();
        return (new(new Fields(Field()), gateway, new NoopSimulationBroadcaster()), gateway);
    }
    private SimulationField Field() => new()
    {
        EventId = _eventId, DistanceId = _distanceId, Title = "Demo", DistanceKm = 0.05,
        Polyline = [new(10, 106), new(10.1, 106.1)],
        Checkpoints = [new("start", "Start", 0, 10, 106, "start"), new("cp", "CP", 0.025, 10.05, 106.05, "cp"), new("finish", "Finish", 0.05, 10.1, 106.1, "finish")],
        Eligible = [new(_a, "001", "A", "chip-a"), new(_b, "002", "B", "chip-b")]
    };
    private sealed class Fields(SimulationField field) : ISimulationFieldSource
    {
        public Task<SimulationField> LoadAsync(Guid eventId, Guid distanceId, CancellationToken ct = default) => Task.FromResult(field);
    }
    private sealed class Courses(SimulationField field) : IRaceCourseReadQuery
    {
        public Task<RaceCourseSnapshot?> GetAsync(Guid eventId, Guid distanceId, CancellationToken ct = default)
            => Task.FromResult<RaceCourseSnapshot?>(new(eventId, distanceId, field.Title, field.DistanceKm,
                field.Polyline.Select(p => new RaceCoursePoint(p.Lat, p.Lng)).ToArray(),
                field.Checkpoints.Select(c => new RaceCourseCheckpoint(c.Id, c.Name, c.Km, c.Lat, c.Lng, c.Type)).ToArray()));
    }
    private sealed class Assignments(EventStaffAssignment? member) : IEventStaffAssignmentQuery
    {
        public EventStaffAssignment? Member { get; set; } = member;
        public Task<IReadOnlyList<EventStaffAssignment>> ListAsync(Guid eventId, Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EventStaffAssignment>>(Member is null ? [] : [Member]);
    }
    private sealed class Gateway : ISimulationCommitGateway
    {
        public SimulationCommitCommand? Last { get; private set; }
        public Task<SimulationCommitBatch> CommitAsync(SimulationCommitCommand command, CancellationToken ct = default)
        {
            Last = command;
            return Task.FromResult(new SimulationCommitBatch(true, null, command.Drafts.Select(d =>
                new SimulationCommitItemResult(d.RegistrationId, SimulationCommitOutcomes.CommittedThisRun, null, d.Decision is null ? 10 : null)).ToArray()));
        }
    }
}
