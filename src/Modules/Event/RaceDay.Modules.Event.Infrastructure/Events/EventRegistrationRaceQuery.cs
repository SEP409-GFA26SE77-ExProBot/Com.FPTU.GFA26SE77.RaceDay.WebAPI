using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Events;

public sealed class EventRegistrationRaceQuery(EventDbContext db) : IEventRegistrationRaceQuery
{
    public async Task<EventRegistrationRaceInfo?> GetByIdAsync(
        Guid registrationId,
        CancellationToken cancellationToken = default)
    {
        var row = await db.Registrations.AsNoTracking()
            .Where(registration => registration.Id == registrationId)
            .Select(registration => new
            {
                registration.Id,
                registration.EventId,
                registration.DistanceId,
                registration.BibNumber,
                registration.Status
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        return new EventRegistrationRaceInfo(
            row.Id,
            row.EventId,
            row.DistanceId,
            row.BibNumber,
            row.Status.ToString());
    }

    public async Task<IReadOnlyList<EventRegistrationRaceCandidate>> ListEligibleByEventAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.Registrations.AsNoTracking()
            .Where(registration =>
                registration.EventId == eventId
                && registration.BibNumber != null
                && (registration.Status == RegistrationStatus.Confirmed
                    || registration.Status == RegistrationStatus.Finished))
            .Select(registration => new
            {
                registration.Id,
                registration.EventId,
                registration.DistanceId,
                registration.BibNumber
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new EventRegistrationRaceCandidate(
                row.Id,
                row.EventId,
                row.DistanceId,
                row.BibNumber!.Trim()))
            .Where(row => row.BibNumber.Length > 0)
            .ToArray();
    }
}
