using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Events;

public sealed class RunnerRaceDayQuery(EventDbContext db) : IRunnerRaceDayQuery
{
    public async Task<RunnerRaceDayInfo?> GetPrimaryAsync(
        Guid runnerUserId,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var row = await (
            from reg in db.Registrations.AsNoTracking()
            join ev in db.RaceEvents.AsNoTracking() on reg.EventId equals ev.Id
            where reg.UserId == runnerUserId
                  && (reg.Status == RegistrationStatus.Confirmed || reg.Status == RegistrationStatus.Finished)
                  && (ev.Status == RaceEventStatus.RegistrationOpen
                      || ev.Status == RaceEventStatus.Closed
                      || ev.Status == RaceEventStatus.Completed)
            orderby Math.Abs(ev.EventDate.DayNumber - today.DayNumber)
            select new { reg, ev }
        ).FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new RunnerRaceDayInfo(
                row.ev.Id,
                row.reg.Id,
                row.reg.BibNumber,
                row.ev.OrganizerUserId,
                row.ev.Status.ToString());
    }

    public async Task<RunnerRaceDayInfo?> GetForEventAsync(
        Guid runnerUserId,
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var row = await (
            from reg in db.Registrations.AsNoTracking()
            join ev in db.RaceEvents.AsNoTracking() on reg.EventId equals ev.Id
            where reg.UserId == runnerUserId
                  && reg.EventId == eventId
                  && (reg.Status == RegistrationStatus.Confirmed || reg.Status == RegistrationStatus.Finished)
            select new { reg, ev }
        ).FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new RunnerRaceDayInfo(
                row.ev.Id,
                row.reg.Id,
                row.reg.BibNumber,
                row.ev.OrganizerUserId,
                row.ev.Status.ToString());
    }
}
