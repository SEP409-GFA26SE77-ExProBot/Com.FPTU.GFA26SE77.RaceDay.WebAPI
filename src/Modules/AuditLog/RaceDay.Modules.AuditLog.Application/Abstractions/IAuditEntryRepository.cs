using RaceDay.Modules.AuditLog.Domain.AuditEntries;

namespace RaceDay.Modules.AuditLog.Application.Abstractions;

public interface IAuditEntryRepository
{
    Task AddAsync(AuditEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditEntry>> ListRecentAsync(int take, CancellationToken cancellationToken = default);
}
