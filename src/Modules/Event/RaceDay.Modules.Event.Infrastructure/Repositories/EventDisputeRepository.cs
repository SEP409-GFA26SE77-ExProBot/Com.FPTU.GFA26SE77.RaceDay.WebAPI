using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Application.Disputes;
using RaceDay.Modules.Event.Domain.Disputes;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Repositories;

public sealed class EventDisputeRepository(EventDbContext db) : IEventDisputeRepository
{
    public async Task AddAsync(EventDispute dispute, CancellationToken cancellationToken = default)
        => await db.Disputes.AddAsync(dispute, cancellationToken);

    public async Task<RunnerPageDto<EventDisputeDto>> ListByRunnerAsync(
        Guid runnerUserId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = from dispute in db.Disputes.AsNoTracking()
                    join race in db.RaceEvents.AsNoTracking() on dispute.EventId equals race.Id
                    where dispute.RunnerUserId == runnerUserId
                    orderby dispute.CreatedAtUtc descending, dispute.Id descending
                    select new { Dispute = dispute, EventName = race.Name };

        var count = await query.CountAsync(cancellationToken);
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new(rows.Select(x => EventDisputeDto.From(x.Dispute, x.EventName)).ToList(), count, page, pageSize);
    }
}
