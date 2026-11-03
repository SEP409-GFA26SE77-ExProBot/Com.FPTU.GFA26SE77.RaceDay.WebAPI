using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence;

public sealed class EmergencyAssignmentRepository(RaceMonitoringDbContext db) : IEmergencyAssignmentRepository
{
    public Task<EmergencyAssignment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.EmergencyAssignments.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<EmergencyAssignment?> GetWithResourceAsync(Guid id, CancellationToken cancellationToken = default)
        => db.EmergencyAssignments
            .AsNoTracking()
            .Include(x => x.EmergencyResource)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<bool> ExistsForResponseAsync(
        Guid emergencyResponseId,
        Guid emergencyResourceId,
        CancellationToken cancellationToken = default)
        => db.EmergencyAssignments.AnyAsync(
            x => x.EmergencyResponseId == emergencyResponseId && x.EmergencyResourceId == emergencyResourceId,
            cancellationToken);

    public async Task<(IReadOnlyList<EmergencyAssignment> Items, int Total)> ListByResponseAsync(
        Guid emergencyResponseId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = db.EmergencyAssignments
            .AsNoTracking()
            .Include(x => x.EmergencyResource)
            .Where(x => x.EmergencyResponseId == emergencyResponseId);
        var safePage = page < 1 ? 1 : page;
        var safePageSize = pageSize < 1 ? 20 : pageSize;
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.AssignedAtUtc)
            .ThenBy(x => x.Id)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<IReadOnlyList<EmergencyAssignment>> ListTrackedByResponseAsync(
        Guid emergencyResponseId,
        CancellationToken cancellationToken = default)
    {
        var items = await db.EmergencyAssignments
            .Where(x => x.EmergencyResponseId == emergencyResponseId)
            .ToListAsync(cancellationToken);
        return items;
    }

    public void Add(EmergencyAssignment assignment) => db.EmergencyAssignments.Add(assignment);

    public void Remove(EmergencyAssignment assignment) => db.EmergencyAssignments.Remove(assignment);
}
