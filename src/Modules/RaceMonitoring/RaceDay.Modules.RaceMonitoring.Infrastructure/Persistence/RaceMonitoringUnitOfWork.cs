using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
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
    }

    private static bool IsDuplicateAssignment(DbUpdateException ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains(
                    "IX_emergency_assignments_ResponseId_ResourceId",
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
