using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Device.Contracts.Sos;
using RaceDay.Modules.Device.Infrastructure.Persistence;

namespace RaceDay.Modules.Device.Infrastructure.Sos;

public sealed class SosAlertScopeQuery(DeviceDbContext db) : ISosAlertScopeQuery
{
    public async Task<SosAlertScope?> GetAsync(Guid sosAlertId, CancellationToken cancellationToken = default)
    {
        var row = await db.SosAlerts.AsNoTracking()
            .Where(x => x.Id == sosAlertId)
            .Select(x => new { x.Id, x.EventId, x.Status })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new SosAlertScope(row.Id, row.EventId, row.Status.ToString());
    }

    public async Task<IReadOnlyList<SosAlertScope>> ListByEventAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.SosAlerts.AsNoTracking()
            .Where(x => x.EventId == eventId)
            .Select(x => new { x.Id, x.EventId, x.Status })
            .ToListAsync(cancellationToken);
        return rows.Select(x => new SosAlertScope(x.Id, x.EventId, x.Status.ToString())).ToList();
    }
}
