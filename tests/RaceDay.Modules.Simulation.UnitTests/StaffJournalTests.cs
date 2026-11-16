using RaceDay.Modules.Event.Contracts.Simulation;
using RaceDay.Modules.Simulation.Application;
using RaceDay.Modules.Simulation.Infrastructure;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Simulation.UnitTests;

public partial class StaffCheckpointTests
{
    [Fact]
    public async Task Journal_captures_actor_and_both_clocks_and_retry_does_not_duplicate()
    {
        var (book, journal) = JournalBook();
        var run = await book.CreateAsync(_eventId, _distanceId, null, 4);
        await book.StartAsync(run.RunId);
        await book.AdvanceRunningAsync(1);
        var operation = Guid.NewGuid();
        var result = await book.RecordDecisionAsync(run.RunId, _a, "DNF", " Injury ", null, _staff, "start",
            operationId: operation, actorName: "Staff A");
        var entry = Assert.Single(journal.Entries);
        Assert.Equal("Staff A", entry.ActorName);
        Assert.Equal("Race-staff", entry.ActorRole);
        Assert.Equal(1, entry.SimSeconds);
        Assert.Equal("Racing", entry.PreviousStatus);
        Assert.Equal("DNF", entry.Status);
        Assert.Equal("Injury", entry.Reason);
        Assert.Equal(entry.SimSeconds, Assert.Single(result.Decisions!).CreatedAtSimSeconds);
        Assert.Equal("Staff A", Assert.Single(result.Decisions!).ActorName);
        await book.RecordDecisionAsync(run.RunId, _a, "DNF", "Injury", null, _staff, "start", operationId: operation);
        Assert.Single(journal.Entries);
        Assert.Single(book.SnapshotOf(run.RunId)!.Decisions!);
        await Assert.ThrowsAsync<DomainException>(() => book.RecordDecisionAsync(run.RunId, _a, "DNF", "Changed", null, _staff, "start", operationId: operation));
    }

    [Fact]
    public async Task Failed_journal_write_does_not_mutate_engine_or_decisions()
    {
        var (book, journal) = JournalBook();
        var run = await book.CreateAsync(_eventId, _distanceId, null, 4);
        journal.FailWrite = true;
        await Assert.ThrowsAsync<IOException>(() => book.RecordDecisionAsync(run.RunId, _a, "DNS", "Absent", null, _staff, "start"));
        var unchanged = book.SnapshotOf(run.RunId)!;
        Assert.Equal("Racing", unchanged.Athletes.Single(a => a.RegistrationId == _a).Motion);
        Assert.Empty(unchanged.Decisions!);
        Assert.Empty(journal.Entries);
        Assert.Equal(run.Version, unchanged.Version);
    }

    [Fact]
    public async Task Dq_review_is_a_separate_durable_event_and_failure_leaves_pending_report()
    {
        var (book, journal) = JournalBook();
        var run = await book.CreateAsync(_eventId, _distanceId, null, 4);
        var report = await book.RecordDecisionAsync(run.RunId, _a, "DQ", "Wrong course", "Photo", _staff, "start");
        var decision = Assert.Single(report.Decisions!);
        Assert.Equal("Racing", report.Athletes.Single(a => a.RegistrationId == _a).Motion);
        await book.StartAsync(run.RunId);
        await book.AdvanceRunningAsync(2);
        journal.FailWrite = true;
        await Assert.ThrowsAsync<IOException>(() => book.ReviewDecisionAsync(run.RunId, decision.Id, true, "Confirmed", Guid.NewGuid()));
        Assert.Equal("Pending", Assert.Single(book.SnapshotOf(run.RunId)!.Decisions!).Resolution);
        journal.FailWrite = false;
        var reviewer = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var reviewed = await book.ReviewDecisionAsync(run.RunId, decision.Id, false, "No violation", reviewer, operationId: operation, actorName: "Organizer B");
        Assert.Equal(2, journal.Entries.Count);
        Assert.Equal("DQ_REPORTED", journal.Entries[0].Kind);
        Assert.Equal("DQ_REJECTED", journal.Entries[1].Kind);
        Assert.Equal(decision.Id, journal.Entries[1].DecisionId);
        Assert.Equal("Organizer B", journal.Entries[1].ActorName);
        Assert.Equal(2, reviewed.Decisions![0].ReviewedAtSimSeconds);
        Assert.Equal("Organizer B", reviewed.Decisions![0].ReviewedByName);
        await book.ReviewDecisionAsync(run.RunId, decision.Id, false, "No violation", reviewer, operationId: operation);
        Assert.Equal(2, journal.Entries.Count);
    }

    [Fact]
    public async Task Export_waits_for_local_pending_and_locks_only_its_checkpoint_for_every_actor()
    {
        var (book, journal) = JournalBook();
        var run = await book.CreateAsync(_eventId, _distanceId, null, 4);
        var reported = await book.RecordDecisionAsync(run.RunId, _a, "DQ", "Review", null, _staff, "start");
        await Assert.ThrowsAsync<DomainException>(() => book.SubmitCheckpointExportAsync(run.RunId, "start", _staff, "Staff"));
        await book.StartAsync(run.RunId);
        await book.AdvanceRunningAsync(200);
        await Assert.ThrowsAsync<DomainException>(() => book.SubmitCheckpointExportAsync(run.RunId, "start", _staff, "Staff"));
        await book.ReviewDecisionAsync(run.RunId, reported.Decisions![0].Id, false, "Cleared", Guid.NewGuid());
        journal.FailWrite = true;
        await Assert.ThrowsAsync<IOException>(() => book.SubmitCheckpointExportAsync(run.RunId, "start", _staff, "Staff"));
        Assert.Empty(journal.Exports);
        journal.FailWrite = false;
        var first = await book.SubmitCheckpointExportAsync(run.RunId, "start", _staff, "Staff");
        Assert.Equal(first, await book.SubmitCheckpointExportAsync(run.RunId, "start", Guid.NewGuid(), "Other"));
        Assert.Single(journal.Entries, e => e.Kind == "CHECKPOINT_SUBMITTED");
        await Assert.ThrowsAsync<DomainException>(() => book.RecordDecisionAsync(run.RunId, _b, "DQ", "Late", null, _staff, "start", organizer: true));
        await Assert.ThrowsAsync<DomainException>(() => book.RecordDecisionAsync(run.RunId, _b, "DQ", "Late", null, _staff, "start"));
        await book.RecordDecisionAsync(run.RunId, _b, "DQ", "Finish violation", null, Guid.NewGuid(), "finish", organizer: true);
        Assert.Equal("DQ_RECORDED", journal.Entries.Last().Kind);
    }

    [Fact]
    public async Task Existing_export_is_sealed_without_creating_a_fake_history_entry()
    {
        var (book, journal) = JournalBook();
        var run = await book.CreateAsync(_eventId, _distanceId, null, 4);
        await book.StartAsync(run.RunId);
        await book.AdvanceRunningAsync(200);
        journal.Exports.Add(book.BuildCheckpointExport(run.RunId, "finish", _staff));
        await book.SubmitCheckpointExportAsync(run.RunId, "finish", _staff, "Staff");
        Assert.Empty(journal.Entries);
        await Assert.ThrowsAsync<DomainException>(() => book.RecordDecisionAsync(run.RunId, _a, "DQ", "Late", null, _staff, "finish", organizer: true));
    }

    [Fact]
    public async Task Journal_survives_reset_and_new_book_while_old_run_cannot_be_mutated()
    {
        var (book, journal) = JournalBook();
        var run = await book.CreateAsync(_eventId, _distanceId, null, 4);
        await book.RecordDecisionAsync(run.RunId, _a, "DNS", "Absent", null, _staff, "start");
        await book.ResetAsync(run.RunId, true);
        Assert.Single(await journal.RunsAsync(_eventId, _distanceId, "start"));
        var restarted = new SimulationSessionBook(new Fields(Field()), new Gateway(), new NoopSimulationBroadcaster(), journal);
        var workspace = new StaffCheckpointWorkspace(new Assignments(new(Guid.NewGuid(), _eventId, "Race", $"map:{_distanceId}:start")), new Courses(Field()), restarted, journal);
        var waiting = await workspace.ReadAsync(_eventId, _staff);
        Assert.Null(waiting.RunId);
        Assert.True(waiting.HistoryVersion > 0);
        Assert.Single((await journal.ReadAsync(new(_eventId, _distanceId, "start", run.RunId))).Items);
        Assert.Empty((await journal.ReadAsync(new(_eventId, _distanceId, "finish", run.RunId))).Items);
        await Assert.ThrowsAsync<DomainException>(() => restarted.RecordDecisionAsync(run.RunId, _b, "DNS", "Old", null, _staff, "start"));
    }

    [Fact]
    public async Task Pending_badge_does_not_leak_other_checkpoint_evidence_and_sealed_projection_has_no_actions()
    {
        var (book, journal) = JournalBook();
        var run = await book.CreateAsync(_eventId, _distanceId, null, 4);
        await book.RecordDecisionAsync(run.RunId, _a, "DQ", "Private", "Private photo", _staff, "start");
        var workspace = new StaffCheckpointWorkspace(new Assignments(new(Guid.NewGuid(), _eventId, "Race", $"map:{_distanceId}:cp")), new Courses(Field()), book, journal);
        var cp = await workspace.ReadAsync(_eventId, _staff);
        var athlete = cp.Athletes.Single(a => a.RegistrationId == _a);
        Assert.True(athlete.HasPendingDq);
        Assert.Null(athlete.PendingDq);
        Assert.False(athlete.CanReport);
        Assert.Empty(cp.Decisions);
        Assert.Equal(0, cp.SegmentFromKm);
        Assert.Equal(0.05, cp.SegmentToKm);
    }

    [Fact]
    public async Task Clock_and_reset_cannot_overtake_a_pending_journal_write()
    {
        var (book, journal) = JournalBook();
        var run = await book.CreateAsync(_eventId, _distanceId, null, 4);
        await book.StartAsync(run.RunId);
        journal.HoldWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var recording = book.RecordDecisionAsync(run.RunId, _a, "DNF", "Stopped", null, _staff, "start");
        await journal.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var ticking = book.AdvanceRunningAsync(1);
        Assert.False(ticking.IsCompleted);
        Assert.Equal("Racing", book.SnapshotOf(run.RunId)!.Athletes.Single(a => a.RegistrationId == _a).Motion);
        await Assert.ThrowsAsync<SimulationBusyException>(() => book.ResetAsync(run.RunId, true));
        journal.HoldWrite.SetResult();
        await Task.WhenAll(recording, ticking).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, journal.Entries.Single().SimSeconds);
        Assert.Equal("Dnf", book.SnapshotOf(run.RunId)!.Athletes.Single(a => a.RegistrationId == _a).Motion);
    }

    private (SimulationSessionBook Book, TestSimulationJournal Journal) JournalBook()
    {
        var journal = new TestSimulationJournal();
        return (new(new Fields(Field()), new Gateway(), new NoopSimulationBroadcaster(), journal), journal);
    }

    [Fact]
    public async Task Assignment_is_rechecked_inside_the_mutation_gate_before_writing()
    {
        var (book, journal) = JournalBook();
        var run = await book.CreateAsync(_eventId, _distanceId, null, 4);
        await Assert.ThrowsAsync<StaffCheckpointAccessException>(() => book.RecordDecisionAsync(run.RunId,
            _a, "DNS", "Absent", null, _staff, "start",
            validateAccess: _ => throw new StaffCheckpointAccessException("Assignment changed")));
        Assert.Empty(journal.Entries);
        Assert.Empty(book.SnapshotOf(run.RunId)!.Decisions!);
    }

}
