using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Events;

public sealed class EventCutoffScheduleQuery(EventDbContext db) : IEventCutoffScheduleQuery
{
    private static readonly RaceEventStatus[] NotRaced =
    [
        RaceEventStatus.Draft,
        RaceEventStatus.PendingApproval,
        RaceEventStatus.Rejected,
        RaceEventStatus.Cancelled,
        RaceEventStatus.Suspended
    ];

    public async Task<IReadOnlyList<EventCutoffSchedule>> ListRaceDayEventsAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        var events = await db.RaceEvents.AsNoTracking()
            .Where(x => x.EventDate >= fromDate && x.EventDate <= toDate && !NotRaced.Contains(x.Status))
            .Select(x => new { x.Id, x.Name, x.EventDate })
            .ToListAsync(cancellationToken);
        if (events.Count == 0)
        {
            return [];
        }

        var eventIds = events.Select(x => x.Id).ToArray();
        var distances = await db.Distances.AsNoTracking()
            .Where(x => eventIds.Contains(x.EventId))
            .Select(x => new { x.Id, x.EventId, x.Name, x.StartAt })
            .ToListAsync(cancellationToken);
        var distanceIds = distances.Select(x => x.Id).ToArray();
        var checkpoints = await db.Checkpoints.AsNoTracking()
            .Where(x => distanceIds.Contains(x.DistanceId))
            .Select(x => new { x.Id, x.DistanceId, x.Name, x.Km, x.SortOrder, x.Type, x.Cutoff })
            .ToListAsync(cancellationToken);

        var checkpointsByDistance = checkpoints.ToLookup(x => x.DistanceId);
        var distancesByEvent = distances.ToLookup(x => x.EventId);

        return events
            .Select(e => new EventCutoffSchedule(
                e.Id,
                e.Name,
                e.EventDate,
                distancesByEvent[e.Id]
                    .Select(d => new EventCutoffDistance(
                        d.Id,
                        d.Name,
                        d.StartAt,
                        checkpointsByDistance[d.Id]
                            .OrderBy(c => c.SortOrder)
                            .Select(c => new EventCutoffCheckpoint(
                                c.Id,
                                c.Name,
                                c.Km,
                                c.SortOrder,
                                c.Type.ToString(),
                                c.Cutoff))
                            .ToArray()))
                    .ToArray()))
            .ToArray();
    }

    public async Task<IReadOnlyList<EventRaceRunner>> ListRaceRunnersAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.Registrations.AsNoTracking()
            .Where(x => x.EventId == eventId
                        && x.Status == RegistrationStatus.Confirmed
                        && x.BibNumber != null)
            .Select(x => new
            {
                x.Id,
                x.DistanceId,
                x.BibNumber,
                x.FullName,
                x.RaceStatus,
                x.StartedAtUtc
            })
            .ToListAsync(cancellationToken);

        return rows
            .Where(x => !string.IsNullOrWhiteSpace(x.BibNumber))
            .Select(x => new EventRaceRunner(
                x.Id,
                x.DistanceId,
                x.BibNumber!.Trim(),
                x.FullName.Trim(),
                x.RaceStatus.ToString(),
                x.StartedAtUtc))
            .ToArray();
    }

    public async Task<int?> GetCheckpointSortOrderAsync(
        Guid checkpointId,
        CancellationToken cancellationToken = default)
        => await db.Checkpoints.AsNoTracking()
            .Where(x => x.Id == checkpointId)
            .Select(x => (int?)x.SortOrder)
            .FirstOrDefaultAsync(cancellationToken);
}
