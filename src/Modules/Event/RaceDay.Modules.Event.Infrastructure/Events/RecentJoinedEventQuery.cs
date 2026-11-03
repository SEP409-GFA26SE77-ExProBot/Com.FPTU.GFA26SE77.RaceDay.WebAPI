using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Application.Disputes;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Events;

public sealed class RecentJoinedEventQuery(EventDbContext db) : IRecentJoinedEventQuery
{
    public async Task<RunnerPageDto<RecentJoinedEventDto>> ListAsync(
        Guid runnerUserId, DateOnly today, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = EligibleEvents(runnerUserId, today);
        var count = await query.CountAsync(cancellationToken);
        var items = await Project(query.OrderByDescending(x => x.EventDate).ThenBy(x => x.Id))
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new(items, count, page, pageSize);
    }

    public Task<RecentJoinedEventDto?> GetAsync(
        Guid runnerUserId, Guid eventId, DateOnly today, CancellationToken cancellationToken = default)
        => Project(EligibleEvents(runnerUserId, today).Where(x => x.Id == eventId))
            .FirstOrDefaultAsync(cancellationToken);

    private IQueryable<RaceEvent> EligibleEvents(Guid runnerUserId, DateOnly today)
    {
        var fromDate = today.AddMonths(-1);
        return db.RaceEvents.AsNoTracking()
            .Where(e => e.EventDate >= fromDate && e.EventDate <= today
                && (e.Status == RaceEventStatus.Approved || e.Status == RaceEventStatus.RegistrationOpen
                    || e.Status == RaceEventStatus.Closed || e.Status == RaceEventStatus.Completed))
            .Where(e => db.Registrations.Any(r => r.EventId == e.Id && r.UserId == runnerUserId
                && (r.Status == RegistrationStatus.Confirmed || r.Status == RegistrationStatus.Finished)));
    }

    private static IQueryable<RecentJoinedEventDto> Project(IQueryable<RaceEvent> query)
        => query.Select(e => new RecentJoinedEventDto(e.Id, e.Name, e.EventDate, e.EndDate, e.Location));
}
