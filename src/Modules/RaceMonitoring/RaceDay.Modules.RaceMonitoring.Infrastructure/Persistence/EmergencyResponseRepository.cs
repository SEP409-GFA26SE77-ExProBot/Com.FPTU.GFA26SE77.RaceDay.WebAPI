using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence;

public sealed class EmergencyResponseRepository(RaceMonitoringDbContext db) : IEmergencyResponseRepository
{
    public Task<EmergencyResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.EmergencyResponses.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<EmergencyResponse> Items, int Total)> ListBySourceSosIdsAsync(
        IReadOnlyCollection<Guid> sourceSosAlertIds,
        EmergencyResponseStatus? status,
        EmergencyPriority? priority,
        EmergencyIncidentType? incidentType,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (sourceSosAlertIds.Count == 0)
        {
            return ([], 0);
        }

        var ids = sourceSosAlertIds.Distinct().ToArray();
        var query = db.EmergencyResponses.AsNoTracking().Where(x => ids.Contains(x.SourceSosAlertId));
        if (status is EmergencyResponseStatus responseStatus)
        {
            query = query.Where(x => x.Status == responseStatus);
        }

        if (priority is EmergencyPriority responsePriority)
        {
            query = query.Where(x => x.Priority == responsePriority);
        }

        if (incidentType is EmergencyIncidentType responseIncident)
        {
            query = query.Where(x => x.IncidentType == responseIncident);
        }

        var safePage = page < 1 ? 1 : page;
        var safePageSize = pageSize < 1 ? 20 : pageSize;
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x =>
                x.Priority == EmergencyPriority.Critical ? 3 :
                x.Priority == EmergencyPriority.High ? 2 :
                x.Priority == EmergencyPriority.Medium ? 1 : 0)
            .ThenBy(x => x.Id)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }
}
