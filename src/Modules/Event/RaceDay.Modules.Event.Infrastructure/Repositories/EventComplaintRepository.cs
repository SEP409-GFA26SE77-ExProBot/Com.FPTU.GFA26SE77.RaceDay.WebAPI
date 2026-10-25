using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

    public async Task<IReadOnlyList<EventComplaint>> ListByEventIdAsync(Guid eventId, CancellationToken cancellationToken = default)
        => await db.Complaints
            .Where(x => x.EventId == eventId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EventComplaint>> ListByEventIdsAsync(IEnumerable<Guid> eventIds, CancellationToken cancellationToken = default)
    {
        var list = eventIds?.ToList() ?? new List<Guid>();
        if (list.Count == 0)
        {
            return await db.Complaints
                .OrderByDescending(x => x.CreatedAtUtc)
                .ToListAsync(cancellationToken);
        }

        return await db.Complaints
            .Where(x => list.Contains(x.EventId))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EventComplaint>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await db.Complaints
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(EventComplaint complaint, CancellationToken cancellationToken = default)
        => await db.Complaints.AddAsync(complaint, cancellationToken);

    public void Update(EventComplaint complaint)
        => db.Complaints.Update(complaint);
}