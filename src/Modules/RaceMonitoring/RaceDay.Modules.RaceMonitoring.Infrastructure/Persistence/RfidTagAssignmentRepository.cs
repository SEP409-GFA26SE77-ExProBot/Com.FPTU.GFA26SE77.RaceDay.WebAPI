using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Domain;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence;

public sealed class RfidTagAssignmentRepository(RaceMonitoringDbContext db) : IRfidTagAssignmentRepository
{
    public Task<RfidTagAssignment?> FindByTagAsync(
        Guid eventId,
        string tagScheme,
        string tagId,
        CancellationToken cancellationToken = default)
    {
        var scheme = tagScheme.Trim();
        var normalized = tagId.Trim();
        return db.RfidTagAssignments.AsNoTracking().FirstOrDefaultAsync(
            row => row.EventId == eventId && row.TagScheme == scheme && row.TagId == normalized,
            cancellationToken);
    }

    public Task<RfidTagAssignment?> FindByRegistrationAsync(
        Guid eventId,
        Guid registrationId,
        CancellationToken cancellationToken = default)
        => db.RfidTagAssignments.AsNoTracking().FirstOrDefaultAsync(
            row => row.EventId == eventId && row.RegistrationId == registrationId,
            cancellationToken);

    public void Add(RfidTagAssignment assignment) => db.RfidTagAssignments.Add(assignment);
}
