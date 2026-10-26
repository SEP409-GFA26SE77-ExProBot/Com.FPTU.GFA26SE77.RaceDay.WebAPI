namespace RaceDay.Modules.Event.Contracts.Finance;

/// <summary>Cross-module read model for Finance (Event.Infrastructure implements).</summary>
public interface IEventFinanceQuery
{
    Task<EventFinanceInfo?> GetEventAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EventFinanceInfo>> ListByOrganizerAsync(Guid organizerUserId, CancellationToken cancellationToken = default);
    Task<RegistrationFinanceInfo?> GetRegistrationAsync(Guid registrationId, CancellationToken cancellationToken = default);
    /** Đăng ký còn hiệu lực sau thanh toán — dùng reconcile ledger. */
    Task<IReadOnlyList<Guid>> ListPaidEligibleRegistrationIdsAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);
}

public sealed record EventFinanceInfo(
    Guid EventId,
    string Name,
    Guid OrganizerUserId,
    string Status,
    DateOnly EventDate,
    TimeOnly? StartTime,
    string? AccountHolder,
    string? AccountNumber,
    string? BankName,
    string? BankBranch,
    string? TransferNote);

public sealed record RegistrationFinanceInfo(
    Guid RegistrationId,
    Guid EventId,
    Guid DistanceId,
    Guid UserId,
    string Status,
    string EventName,
    Guid OrganizerUserId,
    string EventStatus,
    DateOnly EventDate,
    TimeOnly? StartTime);
