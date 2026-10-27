namespace RaceDay.Modules.Event.Contracts.Events;

/// <summary>Cross-module: Device SOS gắn runner với giải đang diễn ra (nếu có).</summary>
public interface IRunnerRaceDayQuery
{
    Task<RunnerRaceDayInfo?> GetPrimaryAsync(Guid runnerUserId, CancellationToken cancellationToken = default);

    Task<RunnerRaceDayInfo?> GetForEventAsync(
        Guid runnerUserId,
        Guid eventId,
        CancellationToken cancellationToken = default);
}

public sealed record RunnerRaceDayInfo(
    Guid EventId,
    Guid RegistrationId,
    string? BibNumber,
    Guid OrganizerUserId,
    string EventStatus);
