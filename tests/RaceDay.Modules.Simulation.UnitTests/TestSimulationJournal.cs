using RaceDay.Modules.Event.Contracts.Simulation;
using RaceDay.Modules.Simulation.Application;

namespace RaceDay.Modules.Simulation.UnitTests;

internal sealed class TestSimulationJournal : ISimulationJournalGateway
{
    public List<SimulationJournalEntry> Entries { get; } = [];
    public List<CheckpointResultExport> Exports { get; } = [];
    public bool FailWrite;
    public TaskCompletionSource? HoldWrite;
    public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<SimulationJournalEntry?> FindOperationAsync(Guid op, CancellationToken ct = default) => Task.FromResult(Entries.SingleOrDefault(e => e.OperationId == op));
    public async Task<SimulationJournalEntry> AppendAsync(SimulationJournalEntry entry, CancellationToken ct = default)
    {
        if (FailWrite) throw new IOException("Database unavailable");
        WriteStarted.TrySetResult();
        if (HoldWrite is not null) await HoldWrite.Task.WaitAsync(ct);
        entry = entry with { Sequence = Entries.Count + 1 };
        Entries.Add(entry);
        return entry;
    }
    public Task<SimulationJournalPage> ReadAsync(SimulationJournalQuery q, CancellationToken ct = default) => Task.FromResult(new SimulationJournalPage(
        Entries.Where(e => e.EventId == q.EventId && e.DistanceId == q.DistanceId && e.CheckpointId == q.CheckpointId && e.RunId == q.RunId).OrderByDescending(e => e.Sequence).ToArray(), null));
    public Task<IReadOnlyList<SimulationJournalRun>> RunsAsync(Guid e, Guid d, string cp, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SimulationJournalRun>>(
        Entries.Where(x => x.EventId == e && x.DistanceId == d && x.CheckpointId == cp).GroupBy(x => x.RunId).Select(g => new SimulationJournalRun(g.Key, g.Max(x => x.OccurredAtUtc))).ToArray());
    public Task<long> VersionAsync(Guid e, Guid d, string cp, CancellationToken ct = default) => Task.FromResult(Entries.Where(x => x.EventId == e && x.DistanceId == d && x.CheckpointId == cp).Select(x => x.Sequence).DefaultIfEmpty().Max());
    public Task<CheckpointResultExport?> GetExportAsync(Guid run, string cp, CancellationToken ct = default) => Task.FromResult(Exports.SingleOrDefault(e => e.RunId == run && e.CheckpointId == cp));
    public async Task<CheckpointResultExport> SubmitExportAsync(CheckpointResultExport export, SimulationJournalEntry entry, CancellationToken ct = default)
    {
        var previous = await GetExportAsync(export.RunId, export.CheckpointId, ct);
        if (previous is not null) return previous;
        await AppendAsync(entry, ct);
        Exports.Add(export);
        return export;
    }
}
