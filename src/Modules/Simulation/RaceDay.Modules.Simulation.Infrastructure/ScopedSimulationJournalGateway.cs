using Microsoft.Extensions.DependencyInjection;
using RaceDay.Modules.Event.Contracts.Simulation;
using RaceDay.Modules.Simulation.Application;

namespace RaceDay.Modules.Simulation.Infrastructure;

public sealed class ScopedSimulationJournalGateway(IServiceScopeFactory scopes) : ISimulationJournalGateway
{
    private async Task<T> Call<T>(Func<ISimulationJournalStore, Task<T>> action)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ISimulationJournalStore>());
    }
    public Task<SimulationJournalEntry?> FindOperationAsync(Guid id, CancellationToken ct = default) => Call(s => s.FindOperationAsync(id, ct));
    public Task<SimulationJournalEntry> AppendAsync(SimulationJournalEntry entry, CancellationToken ct = default) => Call(s => s.AppendAsync(entry, ct));
    public Task<SimulationJournalPage> ReadAsync(SimulationJournalQuery query, CancellationToken ct = default) => Call(s => s.ReadAsync(query, ct));
    public Task<IReadOnlyList<SimulationJournalRun>> RunsAsync(Guid e, Guid d, string cp, CancellationToken ct = default) => Call(s => s.RunsAsync(e, d, cp, ct));
    public Task<long> VersionAsync(Guid e, Guid d, string cp, CancellationToken ct = default) => Call(s => s.VersionAsync(e, d, cp, ct));
    public Task<CheckpointResultExport?> GetExportAsync(Guid run, string cp, CancellationToken ct = default) => Call(s => s.GetExportAsync(run, cp, ct));
    public Task<CheckpointResultExport> SubmitExportAsync(CheckpointResultExport export, SimulationJournalEntry entry, CancellationToken ct = default) => Call(s => s.SubmitExportAsync(export, entry, ct));
}
