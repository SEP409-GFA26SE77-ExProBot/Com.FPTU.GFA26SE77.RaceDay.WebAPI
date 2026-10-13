using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Packages;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Modules.Event.Domain.Staff;

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
    /// <summary>
    /// Thay cự ly + ticketPricing bằng ExecuteDelete/Update + AddRange
    /// (không Clear navigation — tránh concurrency owned Start/Finish).
    /// </summary>
    Task PersistDistanceReplaceAsync(
        Guid eventId,
        IReadOnlyList<EventDistance> distances,
        string? ticketPricingJson,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Thay documents bằng ExecuteDelete + AddRange (tránh concurrency khi Clear navigation).
    /// </summary>
    Task PersistDocumentsReplaceAsync(
        Guid eventId,
        IReadOnlyList<EventDocument> documents,
        CancellationToken cancellationToken = default);
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

public interface IEventPackagePurchaseRepository
{
    Task<EventPackagePurchase?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EventPackagePurchase>> ListByOrganizerAsync(Guid organizerUserId, CancellationToken cancellationToken = default);
    Task AddAsync(EventPackagePurchase purchase, CancellationToken cancellationToken = default);
    void Update(EventPackagePurchase purchase);
}

public interface IPromoCodeRepository
{
    Task<PromoCode?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<PromoCode?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PromoCode>> ListAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PromoCode>> ListActiveAsync(DateTime now, CancellationToken cancellationToken = default);
    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task AddAsync(PromoCode promo, CancellationToken cancellationToken = default);
    void Update(PromoCode promo);
}

public interface IPackageDurationOptionRepository
{
    Task<PackageDurationOption?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PackageDurationOption?> GetActiveByMonthsAsync(int months, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PackageDurationOption>> ListAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PackageDurationOption>> ListActiveAsync(CancellationToken cancellationToken = default);
    Task<bool> ExistsByMonthsAsync(int months, CancellationToken cancellationToken = default);
    Task AddAsync(PackageDurationOption option, CancellationToken cancellationToken = default);
    void Update(PackageDurationOption option);
}

public interface IPackageCapacityOptionRepository
{
    Task<PackageCapacityOption?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PackageCapacityOption?> GetActiveByCapAsync(int maxParticipants, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PackageCapacityOption>> ListAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PackageCapacityOption>> ListActiveAsync(CancellationToken cancellationToken = default);
    Task<bool> ExistsByCapAsync(int maxParticipants, CancellationToken cancellationToken = default);
    Task AddAsync(PackageCapacityOption option, CancellationToken cancellationToken = default);
    void Update(PackageCapacityOption option);
}

public interface IEventStaffMemberRepository
{
    Task<EventStaffMember?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EventStaffMember>> ListByEventAsync(
        Guid eventId,
        EventStaffRole? role = null,
        CancellationToken cancellationToken = default);
    Task<bool> ExistsActiveEmailRoleAsync(
        Guid eventId,
        EventStaffRole role,
        string email,
        CancellationToken cancellationToken = default);
    Task AddAsync(EventStaffMember member, CancellationToken cancellationToken = default);
    void Update(EventStaffMember member);
}
