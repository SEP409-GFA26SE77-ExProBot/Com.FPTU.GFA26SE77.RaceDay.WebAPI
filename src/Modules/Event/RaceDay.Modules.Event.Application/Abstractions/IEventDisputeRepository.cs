using RaceDay.Modules.Event.Application.Disputes;
using RaceDay.Modules.Event.Domain.Disputes;

namespace RaceDay.Modules.Event.Application.Abstractions;

public interface IEventDisputeRepository
{
    Task AddAsync(EventDispute dispute, CancellationToken cancellationToken = default);
    Task<RunnerPageDto<EventDisputeDto>> ListByRunnerAsync(
        Guid runnerUserId, int page, int pageSize, CancellationToken cancellationToken = default);
}

public interface IRecentJoinedEventQuery
{
    Task<RunnerPageDto<RecentJoinedEventDto>> ListAsync(
        Guid runnerUserId, DateOnly today, int page, int pageSize,
        CancellationToken cancellationToken = default);
    Task<RecentJoinedEventDto?> GetAsync(
        Guid runnerUserId, Guid eventId, DateOnly today, CancellationToken cancellationToken = default);
}
