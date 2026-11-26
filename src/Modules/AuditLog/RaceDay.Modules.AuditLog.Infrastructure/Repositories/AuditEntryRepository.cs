using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.AuditLog.Application.Abstractions;
using RaceDay.Modules.AuditLog.Domain.AuditEntries;
using RaceDay.Modules.AuditLog.Infrastructure.Persistence;

namespace RaceDay.Modules.AuditLog.Infrastructure.Repositories;

/// <summary>Append-only Postgres — SaveChanges ngay khi ghi, không classic UoW.</summary>
public sealed class AuditEntryRepository(AuditDbContext db) : IAuditEntryRepository
{
    public async Task AddAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        db.AuditEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEntry>> ListRecentAsync(int take, CancellationToken cancellationToken = default)
    {
        return await db.AuditEntries.AsNoTracking()
            .OrderByDescending(x => x.OccurredOnUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<AuditEntry> Items, long Total)> ListPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = db.AuditEntries.AsNoTracking();
        var total = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.OccurredOnUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }
}
