using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Contracts.Simulation;
using RaceDay.Modules.Simulation.Contracts;
using RaceDay.Modules.Simulation.Domain;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Simulation.Application;

public interface ISimulationFieldSource
{
    Task<SimulationField> LoadAsync(Guid eventId, Guid distanceId, CancellationToken cancellationToken = default);
}

public sealed class SimulationField
{
    public required Guid EventId { get; init; }
    public required Guid DistanceId { get; init; }
    public required string Title { get; init; }
    public required double DistanceKm { get; init; }
    public string? CourseError { get; init; }
    public required IReadOnlyList<SimulationCoursePoint> Polyline { get; init; }
    public required IReadOnlyList<SimulationCourseCheckpoint> Checkpoints { get; init; }
    public required IReadOnlyList<SimulationAthleteSeed> Eligible { get; init; }
}

public interface ISimulationCommitGateway
{
    Task<SimulationCommitBatch> CommitAsync(SimulationCommitCommand command, CancellationToken cancellationToken = default);
}

public interface ISimulationBroadcaster
{
    Task PublishAsync(SimulationSnapshotDto snapshot, CancellationToken cancellationToken = default);

    Task PublishReplacedAsync(SimulationRunReplacedDto notice, CancellationToken cancellationToken = default);
}

public sealed class NoopSimulationBroadcaster : ISimulationBroadcaster
{
    public Task PublishAsync(SimulationSnapshotDto snapshot, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task PublishReplacedAsync(SimulationRunReplacedDto notice, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

public interface ISimulationAuthorizer
{
    Task EnsureCanManageAsync(Guid eventId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Only the event owner controls the simulation. Staff use the assigned checkpoint API.
/// The actor is the authenticated user, never a client-supplied id.
/// </summary>
public sealed class SimulationAuthorizer(
    ICurrentUserAccessor currentUser,
    IEventOrganizerAccessQuery access) : ISimulationAuthorizer
{
    public async Task EnsureCanManageAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        if ((await access.GetAsync(eventId, cancellationToken))?.OrganizerUserId != userId)
        {
            throw new DomainException("Bạn không có quyền điều khiển giả lập của giải này.");
        }
    }
}

public sealed class SimulationBusyException : Exception
{
    public SimulationBusyException()
        : base("Lượt giả lập đang ghi nhận kết quả. Hãy đợi xong rồi thử lại.")
    {
    }
}

public static class SimulationAccess
{
    public static async Task AuthorizeAsync(
        SimulationSessionBook book,
        ISimulationAuthorizer authorizer,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var eventId = book.EventIdOf(runId) ?? throw new DomainException("Không tìm thấy lượt giả lập.");
        await authorizer.EnsureCanManageAsync(eventId, cancellationToken);
    }

    public static async Task<SimulationSnapshotDto> ReadAsync(
        SimulationSessionBook book,
        ISimulationAuthorizer authorizer,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(book, authorizer, runId, cancellationToken);
        return book.SnapshotOf(runId) ?? throw new DomainException("Không tìm thấy lượt giả lập.");
    }
}

/// <summary>
/// Checks the event permission, joins the run group, then reads the latest snapshot.
/// The snapshot is taken only after the join so a Pause in that window is included.
/// Broadcasts and the JoinRun payload can arrive in either order; the payload still carries
/// run id, generation and version so the client can drop an older snapshot.
/// </summary>
public static class SimulationHubJoin
{
    public const string ReplacedMessage =
        "Lượt giả lập đã được thay thế. Đọc lại preview để lấy activeRunId.";

    public static async Task<SimulationSnapshotDto> JoinAsync(
        SimulationSessionBook book,
        ISimulationAuthorizer authorizer,
        Guid runId,
        Func<Task> addToGroup,
        Func<Task> removeFromGroup,
        CancellationToken cancellationToken = default)
    {
        await SimulationAccess.AuthorizeAsync(book, authorizer, runId, cancellationToken);
        await addToGroup();
        var snapshot = book.SnapshotOf(runId);
        if (snapshot is null || snapshot.RunId != runId)
        {
            try
            {
                await removeFromGroup();
            }
            catch (Exception)
            {
                // Losing the replacement error would leave the client on a dead run id.
            }

            throw new DomainException(ReplacedMessage);
        }

        return snapshot;
    }
}
