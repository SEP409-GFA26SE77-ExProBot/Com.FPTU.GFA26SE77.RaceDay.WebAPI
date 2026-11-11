using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using RaceDay.Modules.Simulation.Application;
using RaceDay.Modules.Simulation.Contracts;
using RaceDay.Shared.Domain.Exceptions;
using RaceDay.Shared.Web.Auth;

namespace RaceDay.Modules.Simulation.Api;

public interface ISimulationClient
{
    Task Snapshot(SimulationSnapshotDto snapshot);

    Task RunReplaced(SimulationRunReplacedDto notice);
}

[Authorize(Policy = AuthPolicies.HumanUser)]
public sealed class SimulationHub(SimulationSessionBook book, ISimulationAuthorizer authorizer, IServiceScopeFactory scopes) : Hub<ISimulationClient>
{
    // Resolve permissions on every update, including while waiting/paused/finished.
    // A connection never joins the organizer group or receives the full run snapshot.
    public async IAsyncEnumerable<StaffCheckpointSnapshotDto> WatchCheckpoint(Guid eventId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rawId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Context.User?.FindFirstValue("sub");
        if (!Guid.TryParse(rawId, out var userId)) throw new HubException("Cần đăng nhập.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Context.ConnectionAborted);
        while (!linked.IsCancellationRequested)
        {
            StaffCheckpointSnapshotDto snapshot;
            await using (var scope = scopes.CreateAsyncScope())
            {
                try
                {
                    snapshot = await scope.ServiceProvider.GetRequiredService<IStaffCheckpointWorkspace>()
                        .ReadAsync(eventId, userId, linked.Token);
                }
                catch (DomainException ex) { throw new HubException(ex.Message); }
            }
            yield return snapshot;
            await Task.Delay(1000, linked.Token);
        }
    }

    public async Task JoinRun(Guid runId)
    {
        try
        {
            var group = SignalRSimulationBroadcaster.GroupName(runId);
            var snapshot = await SimulationHubJoin.JoinAsync(
                book,
                authorizer,
                runId,
                () => Groups.AddToGroupAsync(Context.ConnectionId, group),
                () => Groups.RemoveFromGroupAsync(Context.ConnectionId, group),
                Context.ConnectionAborted);
            await Clients.Caller.Snapshot(snapshot);
        }
        catch (DomainException ex)
        {
            throw new HubException(ex.Message);
        }
    }

    public Task LeaveRun(Guid runId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, SignalRSimulationBroadcaster.GroupName(runId));
}

public sealed class SignalRSimulationBroadcaster(IHubContext<SimulationHub, ISimulationClient> hub) : ISimulationBroadcaster
{
    private readonly ConcurrentDictionary<Guid, long> _sent = new();

    public static string GroupName(Guid runId) => $"sim:{runId:N}";

    public async Task PublishAsync(SimulationSnapshotDto snapshot, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var current = _sent.GetOrAdd(snapshot.RunId, 0);
            if (snapshot.Version <= current)
            {
                return;
            }

            if (_sent.TryUpdate(snapshot.RunId, snapshot.Version, current))
            {
                await hub.Clients.Group(GroupName(snapshot.RunId)).Snapshot(snapshot);
                return;
            }
        }
    }

    public async Task PublishReplacedAsync(SimulationRunReplacedDto notice, CancellationToken cancellationToken = default)
    {
        _sent.TryRemove(notice.PreviousRunId, out _);
        _sent[notice.RunId] = 0;
        await hub.Clients.Group(GroupName(notice.PreviousRunId)).RunReplaced(notice);
    }
}
