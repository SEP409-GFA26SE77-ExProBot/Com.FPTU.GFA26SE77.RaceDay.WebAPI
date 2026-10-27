using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Complaints;

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Repositories;

public sealed class EventComplaintRepository(EventDbContext db) : IEventComplaintRepository
{
    public Task<EventComplaint?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.Complaints.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task AddAsync(EventComplaint complaint, CancellationToken cancellationToken = default)
        => await db.Complaints.AddAsync(complaint, cancellationToken);

    public void Update(EventComplaint complaint)
        => db.Complaints.Update(complaint);

    public async Task<(IReadOnlyList<EventComplaint> Items, int TotalCount, int PendingCount)> SearchOrganizerComplaintsAsync(
        IEnumerable<Guid> eventIds, bool isAdmin, string? status, DateTime? fromDate, DateTime? toDate, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Complaints.AsQueryable();

        // Nếu không phải admin, bắt buộc phải thuộc danh sách eventIds của organizer đó
        if (!isAdmin)
        {
            var eventIdList = eventIds.ToList();
            if (eventIdList.Count == 0) return (new List<EventComplaint>(), 0, 0); // Không quản lý giải nào
            query = query.Where(x => eventIdList.Contains(x.EventId));
        }
        else if (eventIds.Any())
        {
            // Admin lọc theo eventId cụ thể
            var eventIdList = eventIds.ToList();
            query = query.Where(x => eventIdList.Contains(x.EventId));
        }

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<ComplaintStatus>(status, true, out var parsedStatus))
            query = query.Where(x => x.Status == parsedStatus);

        if (fromDate.HasValue) query = query.Where(x => x.CreatedAtUtc >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(x => x.CreatedAtUtc <= toDate.Value);

        int totalCount = await query.CountAsync(ct);
        int pendingCount = await query.CountAsync(x => x.Status == ComplaintStatus.Pending, ct);

        var items = await query.OrderByDescending(x => x.CreatedAtUtc)
                               .Skip((page - 1) * pageSize).Take(pageSize)
                               .ToListAsync(ct);
        return (items, totalCount, pendingCount);
    }

    public async Task<(IReadOnlyList<EventComplaint> Items, int TotalCount)> SearchMyComplaintsAsync(
        Guid userId, Guid? eventId, string? status, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Complaints.Where(x => x.UserId == userId);

        if (eventId.HasValue) query = query.Where(x => x.EventId == eventId.Value);

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<ComplaintStatus>(status, true, out var parsedStatus))
            query = query.Where(x => x.Status == parsedStatus);

        int totalCount = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAtUtc)
                               .Skip((page - 1) * pageSize).Take(pageSize)
                               .ToListAsync(ct);
        return (items, totalCount);
    }
}