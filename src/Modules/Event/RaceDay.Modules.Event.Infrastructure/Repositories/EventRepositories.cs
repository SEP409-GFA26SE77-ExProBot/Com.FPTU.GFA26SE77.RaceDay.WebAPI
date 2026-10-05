using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Repositories;

public sealed class RaceEventRepository(EventDbContext db) : IRaceEventRepository
{
    public Task<RaceEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.RaceEvents.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<RaceEvent?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
        => db.RaceEvents
            .Include(x => x.Distances).ThenInclude(d => d.Checkpoints)
            .Include(x => x.Documents)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<RaceEvent>> ListByStatusAsync(RaceEventStatus status, CancellationToken cancellationToken = default)
        => await db.RaceEvents
            .Include(x => x.Distances).ThenInclude(d => d.Checkpoints)
            .Include(x => x.Documents)
            .Where(x => x.Status == status)
            .OrderByDescending(x => x.SubmittedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RaceEvent>> ListPublicAsync(CancellationToken cancellationToken = default)
        => await db.RaceEvents
            .Include(x => x.Distances)
            .Where(x => x.Status == RaceEventStatus.Approved
                        || x.Status == RaceEventStatus.RegistrationOpen
                        || x.Status == RaceEventStatus.Closed
                        || x.Status == RaceEventStatus.Completed)
            .OrderBy(x => x.EventDate)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RaceEvent>> ListByOrganizerAsync(Guid organizerUserId, CancellationToken cancellationToken = default)
        => await db.RaceEvents
            .Include(x => x.Distances)
            .Where(x => x.OrganizerUserId == organizerUserId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(RaceEvent raceEvent, CancellationToken cancellationToken = default)
        => await db.RaceEvents.AddAsync(raceEvent, cancellationToken);

    public void Update(RaceEvent raceEvent) => db.RaceEvents.Update(raceEvent);
}

public sealed class EventRegistrationRepository(EventDbContext db) : IEventRegistrationRepository
{
    public Task<EventRegistration?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.Registrations.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<EventRegistration>> ListByEventAsync(Guid eventId, CancellationToken cancellationToken = default)
        => await db.Registrations
            .Where(x => x.EventId == eventId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EventRegistration>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await db.Registrations
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsForUserDistanceAsync(Guid eventId, Guid distanceId, Guid userId, CancellationToken cancellationToken = default)
        => db.Registrations.AnyAsync(
            x => x.EventId == eventId && x.DistanceId == distanceId && x.UserId == userId
                 && x.Status != RegistrationStatus.Cancelled && x.Status != RegistrationStatus.Rejected,
            cancellationToken);

    public async Task<IReadOnlyList<EventRegistration>> ListActiveByUserEventAsync(
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken = default)
        => await db.Registrations
            .Where(x => x.EventId == eventId
                        && x.UserId == userId
                        && x.Status != RegistrationStatus.Cancelled
                        && x.Status != RegistrationStatus.Rejected)
            .ToListAsync(cancellationToken);

    public async Task<string> NextBibNumberAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var bibs = await db.Registrations
            .Where(x => x.EventId == eventId && x.BibNumber != null)
            .Select(x => x.BibNumber!)
            .ToListAsync(cancellationToken);

        var max = 1000;
        foreach (var b in bibs)
        {
            if (int.TryParse(b, out var n) && n > max)
            {
                max = n;
            }
        }

        return (max + 1).ToString();
    }

    public async Task AddAsync(EventRegistration registration, CancellationToken cancellationToken = default)
        => await db.Registrations.AddAsync(registration, cancellationToken);

    public void Update(EventRegistration registration) => db.Registrations.Update(registration);
}
