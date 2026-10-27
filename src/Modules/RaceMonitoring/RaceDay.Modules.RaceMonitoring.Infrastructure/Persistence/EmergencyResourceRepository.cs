using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence;

public sealed class EmergencyResourceRepository(RaceMonitoringDbContext db) : IEmergencyResourceRepository
{
    public Task<EmergencyResource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.EmergencyResources.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<EmergencyResource> Items, int Total)> ListByEventAsync(
        Guid eventId,
        EmergencyResourceStatus? status,
        EmergencyResourceType? resourceType,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = db.EmergencyResources.AsNoTracking().Where(x => x.EventId == eventId);
        if (status is EmergencyResourceStatus resourceStatus)
        {
            query = query.Where(x => x.Status == resourceStatus);
        }

        if (resourceType is EmergencyResourceType type)
        {
            query = query.Where(x => x.ResourceType == type);
        }

        var safePage = page < 1 ? 1 : page;
        var safePageSize = pageSize < 1 ? 20 : pageSize;
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.ResourceCode)
            .ThenBy(x => x.Id)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }
}
