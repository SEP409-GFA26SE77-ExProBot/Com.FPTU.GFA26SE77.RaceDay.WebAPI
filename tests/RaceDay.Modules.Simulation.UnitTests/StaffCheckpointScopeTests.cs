using RaceDay.Modules.Simulation.Application;
using RaceDay.Modules.Simulation.Contracts;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Simulation.UnitTests;

public partial class StaffCheckpointTests
{
    [Theory]
    [InlineData(0, "start")]
    [InlineData(0.000001, "cp")]
    [InlineData(0.024999, "cp")]
    [InlineData(0.025, "cp")]
    [InlineData(0.025001, "finish")]
    [InlineData(0.049999, "finish")]
    [InlineData(0.05, "finish")]
    public async Task Each_position_belongs_to_exactly_one_checkpoint(double km, string owner)
    {
        var (book, _) = CreateBook();
        var run = await book.CreateAsync(_eventId, _distanceId, null, 4);
        run = run with { Phase = "Paused", Athletes = run.Athletes.Select(a => a with { CourseKm = km }).ToArray() };
        var projections = run.Checkpoints.Select(c => Project(run, c.Id)).ToArray();
        Assert.Equal(owner, Assert.Single(projections, p => p.Athletes.First().IsInAssignedSegment).Checkpoint.Id);
        foreach (var p in projections)
        {
            Assert.Equal(p.Checkpoint.Km, p.SegmentToKm);
            Assert.All(p.Athletes, a =>
            {
                Assert.Equal(p.Checkpoint.Id == owner, a.CanDnf);
                Assert.Equal(p.Checkpoint.Id == owner, a.CanReport);
            });
        }
        Assert.Equal(0, projections[0].SegmentFromKm);
        Assert.Equal(0, projections[1].SegmentFromKm);
        Assert.Equal(0.025, projections[2].SegmentFromKm);
    }

    [Fact]
    public async Task Ready_is_start_only_and_finished_runner_cannot_dnf_but_can_report()
    {
        var (book, _) = CreateBook();
        var run = await book.CreateAsync(_eventId, _distanceId, null, 4);
        foreach (var c in run.Checkpoints)
            Assert.All(Project(run, c.Id).Athletes, a =>
            {
                Assert.Equal(c.Type == "start", a.IsInAssignedSegment);
                Assert.Equal(c.Type == "start", a.CanDns);
                Assert.Equal(c.Type == "start", a.CanReport);
                Assert.False(a.CanDnf);
            });
        await book.StartAsync(run.RunId);
        await book.AdvanceRunningAsync(200);
        run = book.SnapshotOf(run.RunId)!;
        foreach (var c in run.Checkpoints)
            Assert.All(Project(run, c.Id).Athletes, a =>
            {
                Assert.Equal(c.Type == "finish", a.IsInAssignedSegment);
                Assert.True(a.CanReport);
                Assert.False(a.CanDnf);
                Assert.False(a.CanDns);
            });
        Assert.All(Project(run, "cp", submitted: true).Athletes, a => Assert.False(a.CanReport));
        Assert.All(Project(run with { DecisionsEditable = false }, "cp").Athletes, a => Assert.False(a.CanReport));
    }

    [Fact]
    public async Task Commands_recheck_current_segment_and_allow_post_passage_dq_until_sealed()
    {
        var (book, journal) = JournalBook();
        var run = await book.CreateAsync(_eventId, _distanceId, null, 4);
        await book.StartAsync(run.RunId);
        // At Start, the next checkpoint cannot act yet.
        await Assert.ThrowsAsync<DomainException>(() => book.RecordDecisionAsync(run.RunId, _a, "DQ", "Too early", null, _staff, "cp"));
        await book.AdvanceRunningAsync(1);
        var moving = book.SnapshotOf(run.RunId)!;
        Assert.True(Project(moving, "cp").Athletes.First().CanDnf);
        await Assert.ThrowsAsync<DomainException>(() => book.RecordDecisionAsync(run.RunId, _a, "DNF", "Left Start", null, _staff, "start"));
        await Assert.ThrowsAsync<DomainException>(() => book.RecordDecisionAsync(run.RunId, _a, "DQ", "Not at Finish", null, _staff, "finish"));
        Assert.Empty(journal.Entries);
        await book.AdvanceRunningAsync(200);
        await Assert.ThrowsAsync<DomainException>(() => book.RecordDecisionAsync(run.RunId, _a, "DNF", "Left CP", null, _staff, "cp"));
        await Assert.ThrowsAsync<DomainException>(() => book.RecordDecisionAsync(run.RunId, _a, "DNF", "Already finished", null, _staff, "finish"));
        var op = Guid.NewGuid();
        var reported = await book.RecordDecisionAsync(run.RunId, _a, "DQ", "Violation at CP", null, _staff, "cp", operationId: op);
        Assert.Equal("Finished", reported.Athletes.Single(a => a.RegistrationId == _a).Motion);
        Assert.False(Project(reported, "cp").Athletes.Single(a => a.RegistrationId == _a).IsInAssignedSegment);
        Assert.False(Project(reported, "start").Athletes.Single(a => a.RegistrationId == _a).CanReport);
        await book.RecordDecisionAsync(run.RunId, _a, "DQ", "Violation at CP", null, _staff, "cp", operationId: op);
        Assert.Single(journal.Entries);
        var decision = Assert.Single(reported.Decisions!);
        await book.ReviewDecisionAsync(run.RunId, decision.Id, false, "Cleared", Guid.NewGuid());
        await book.SubmitCheckpointExportAsync(run.RunId, "cp", _staff, "Staff");
        await Assert.ThrowsAsync<DomainException>(() => book.RecordDecisionAsync(run.RunId, _b, "DQ", "Sealed", null, _staff, "cp"));
        await book.RecordDecisionAsync(run.RunId, _b, "DQ", "Start incident", null, _staff, "start");
        Assert.Equal("start", journal.Entries.Last().CheckpointId);
        await book.ResetAsync(run.RunId, true);
        await Assert.ThrowsAsync<DomainException>(() => book.RecordDecisionAsync(run.RunId, _a, "DQ", "Old run", null, _staff, "cp"));
        Assert.Equal(4, journal.Entries.Count);
    }

    [Fact]
    public async Task Passing_cp_while_running_removes_dnf_but_keeps_dq_permission()
    {
        var (book, journal) = JournalBook();
        var run = await book.CreateAsync(_eventId, _distanceId, null, 4);
        await book.SetBaseSpeedAsync(run.RunId, _a, 40);
        await book.SetBaseSpeedAsync(run.RunId, _b, 0);
        await book.StartAsync(run.RunId);
        await book.AdvanceRunningAsync(1);
        Assert.True(Project(book.SnapshotOf(run.RunId)!, "cp").Athletes.Single(a => a.RegistrationId == _a).CanDnf);
        for (var i = 0; i < 200 && book.SnapshotOf(run.RunId)!.Athletes.Single(a => a.RegistrationId == _a).CourseKm <= 0.025; i++)
            await book.AdvanceRunningAsync(0.25);
        run = book.SnapshotOf(run.RunId)!;
        Assert.Equal("Running", run.Phase);
        var athlete = Project(run, "cp").Athletes.Single(a => a.RegistrationId == _a);
        Assert.NotNull(athlete.PassedAtSeconds);
        Assert.False(athlete.IsInAssignedSegment);
        Assert.False(athlete.CanDnf);
        Assert.True(athlete.CanReport);
        await Assert.ThrowsAsync<DomainException>(() => book.RecordDecisionAsync(run.RunId, _a, "DNF", "Late DNF", null, _staff, "cp"));
        await book.RecordDecisionAsync(run.RunId, _a, "DQ", "Incident at CP", null, _staff, "cp");
        Assert.Equal("DQ_REPORTED", Assert.Single(journal.Entries).Kind);
    }

    private StaffCheckpointSnapshotDto Project(SimulationSnapshotDto run, string checkpointId, bool submitted = false) =>
        StaffCheckpointProjection.Build(_eventId, "Race", _distanceId, run.Title, run.DistanceKm,
            run.Polyline, run.Checkpoints.Single(c => c.Id == checkpointId), run, submitted);
}
