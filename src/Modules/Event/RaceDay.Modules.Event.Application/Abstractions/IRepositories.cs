using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;

namespace RaceDay.Modules.Event.Application.Abstractions;

public interface IRaceEventRepository
{
    Task<RaceEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RaceEvent?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RaceEvent>> ListByStatusAsync(RaceEventStatus status, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RaceEvent>> ListPublicAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RaceEvent>> ListByOrganizerAsync(Guid organizerUserId, CancellationToken cancellationToken = default);
    Task AddAsync(RaceEvent raceEvent, CancellationToken cancellationToken = default);
    void Update(RaceEvent raceEvent);
}

public interface IEventRegistrationRepository
{
    Task<EventRegistration?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EventRegistration>> ListByEventAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EventRegistration>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> ExistsForUserDistanceAsync(Guid eventId, Guid distanceId, Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EventRegistration>> ListActiveByUserEventAsync(Guid eventId, Guid userId, CancellationToken cancellationToken = default);
    Task<string> NextBibNumberAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task AddAsync(EventRegistration registration, CancellationToken cancellationToken = default);
    void Update(EventRegistration registration);
}
