using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Events;

public sealed class EventBibRosterQuery(EventDbContext db) : IEventBibRosterQuery
{
    public async Task<EventBibRosterSnapshot> GetConfirmedBibsAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var bibs = await db.Registrations.AsNoTracking()
            .Where(x =>
                x.EventId == eventId
                && x.BibNumber != null
                && (x.Status == RegistrationStatus.Confirmed
                    || x.Status == RegistrationStatus.Finished))
            .Select(x => x.BibNumber!)
            .ToListAsync(cancellationToken);

        var normalized = bibs
            .Select(b => b.Trim().ToUpperInvariant())
            .Where(b => b.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(b => b, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var version = normalized.Count == 0
            ? "empty"
            : Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(string.Join('|', normalized))))[..16];

        return new EventBibRosterSnapshot(eventId, version, normalized);
    }
}
