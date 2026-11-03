using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence;

public sealed class RaceMonitoringUnitOfWork(RaceMonitoringDbContext dbContext) : IRaceMonitoringUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateAssignment(ex))
        {
            throw new DomainException("Nguồn lực này đã nằm trong danh sách phân công của hồ sơ.");
        }
        catch (DbUpdateException ex) when (IsDuplicateSourceSos(ex))
        {
            DetachAddedEmergencyResponses();
            return 0;
        }
        catch (DbUpdateException ex) when (IsDuplicateCheckpointScan(ex) || IsDuplicateRaceEventLog(ex))
        {
            // Hai request cùng ScanId: không ghi thêm log và không phát SSE.
            DetachAdded<CheckpointScan>();
            DetachAdded<RaceEventLog>();
            return 0;
        }
    }

    private void DetachAddedEmergencyResponses()
    {
        var added = dbContext.ChangeTracker.Entries<EmergencyResponse>()
            .Where(x => x.State == EntityState.Added)
            .ToList();
        foreach (var entry in added)
        {
            entry.State = EntityState.Detached;
        }
    }

    private static bool IsDuplicateAssignment(DbUpdateException ex) =>
        Contains(ex, "IX_emergency_assignments_ResponseId_ResourceId");

    private void DetachAdded<TEntity>()
        where TEntity : class
    {
        var added = dbContext.ChangeTracker.Entries<TEntity>()
            .Where(x => x.State == EntityState.Added)
            .ToList();
        foreach (var entry in added)
        {
            entry.State = EntityState.Detached;
        }
    }

    private static bool IsDuplicateSourceSos(DbUpdateException ex) =>
        Contains(ex, "IX_emergency_responses_SourceSosAlertId");

    private static bool IsDuplicateCheckpointScan(DbUpdateException ex) =>
        Contains(ex, "PK_checkpoint_scans");

    private static bool IsDuplicateRaceEventLog(DbUpdateException ex) =>
        Contains(ex, "IX_race_event_logs_Source");

    private static bool Contains(Exception ex, string constraintName)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains(constraintName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
