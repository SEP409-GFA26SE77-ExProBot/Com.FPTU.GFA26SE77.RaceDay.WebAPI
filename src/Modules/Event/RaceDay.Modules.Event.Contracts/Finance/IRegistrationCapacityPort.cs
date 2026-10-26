namespace RaceDay.Modules.Event.Contracts.Finance;

/// <summary>Release distance slot after refund / reject of a paid registration.</summary>
public interface IRegistrationCapacityPort
{
    Task ReleaseSlotAsync(Guid registrationId, CancellationToken cancellationToken = default);
    Task MarkRegistrationCancelledAfterRefundAsync(Guid registrationId, CancellationToken cancellationToken = default);
}
