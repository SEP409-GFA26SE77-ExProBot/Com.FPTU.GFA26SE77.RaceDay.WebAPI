namespace RaceDay.Modules.Event.Contracts.Events;

/// <summary>Đọc đăng ký để ghi nhận checkpoint mà không mở DbContext của Event.</summary>
public interface IEventRegistrationRaceQuery
{
    Task<EventRegistrationRaceInfo?> GetByIdAsync(
        Guid registrationId,
        CancellationToken cancellationToken = default);

    /// <summary>VĐV confirmed hoặc finished, đã có BIB, trên một giải.</summary>
    Task<IReadOnlyList<EventRegistrationRaceCandidate>> ListEligibleByEventAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);
}

public sealed record EventRegistrationRaceInfo(
    Guid RegistrationId,
    Guid EventId,
    Guid DistanceId,
    string? BibNumber,
    string Status);

public sealed record EventRegistrationRaceCandidate(
    Guid RegistrationId,
    Guid EventId,
    Guid DistanceId,
    string BibNumber);
