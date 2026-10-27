using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RaceDay.Modules.Event.Domain.Complaints;

namespace RaceDay.Modules.Event.Application.Abstractions;

public interface IEventComplaintRepository
{
    Task<EventComplaint?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(EventComplaint complaint, CancellationToken cancellationToken = default);
    void Update(EventComplaint complaint);

    // API Organizer
    Task<(IReadOnlyList<EventComplaint> Items, int TotalCount, int PendingCount)> SearchOrganizerComplaintsAsync(
        IEnumerable<Guid> eventIds, bool isAdmin, string? status, DateTime? fromDate, DateTime? toDate, int page, int pageSize, CancellationToken ct);

    // API Runner
    Task<(IReadOnlyList<EventComplaint> Items, int TotalCount)> SearchMyComplaintsAsync(
        Guid userId, Guid? eventId, string? status, int page, int pageSize, CancellationToken ct);
}