using System;
using System.Collections.Generic;
using System.Text;
using RaceDay.Modules.Event.Domain.Complaints;
public interface IEventComplaintRepository
{
    Task<EventComplaint?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EventComplaint>> ListByEventIdAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EventComplaint>> ListByEventIdsAsync(IEnumerable<Guid> eventIds, CancellationToken cancellationToken = default);
    Task AddAsync(EventComplaint complaint, CancellationToken cancellationToken = default);
    void Update(EventComplaint complaint);
    Task<IReadOnlyList<EventComplaint>> ListByUserAsync(Guid userId, CancellationToken cancellationToken);
}