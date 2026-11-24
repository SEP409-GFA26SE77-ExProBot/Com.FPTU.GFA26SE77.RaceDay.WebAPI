using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.RunnerMonitoring;

namespace RaceDay.Modules.RaceMonitoring.Application.Abstractions;

public interface ICutoffWarningRepository
{
    /// <summary>Tracked, for dismiss.</summary>
    Task<CutoffWarning?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Read-only, for detail queries.</summary>
    Task<CutoffWarning?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every (registration, checkpoint) pair that already has a warning, whatever its status.</summary>
    Task<IReadOnlyList<CutoffWarningKey>> ListKeysByEventAsync(Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>Tracked active warnings of one event.</summary>
    Task<IReadOnlyList<CutoffWarning>> ListActiveByEventAsync(Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>Tracked active warnings of one registration.</summary>
    Task<IReadOnlyList<CutoffWarning>> ListActiveByRegistrationAsync(
        Guid registrationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CutoffWarningEventSummary>> SummarizeEventsAsync(CancellationToken cancellationToken = default);

    Task<CutoffWarningPage> ListAsync(CutoffWarningFilter filter, CancellationToken cancellationToken = default);

    void Add(CutoffWarning warning);
}

public sealed record CutoffWarningKey(Guid RegistrationId, Guid CheckpointId);

public sealed record CutoffWarningEventSummary(
    Guid EventId,
    string EventName,
    int ActiveCount,
    int TotalCount,
    DateTime LastDetectedAtUtc);

public sealed record CutoffWarningFilter(
    IReadOnlyCollection<Guid> EventIds,
    CutoffWarningStatus? Status,
    string? Search,
    int Page,
    int PageSize);

public sealed record CutoffWarningPage(IReadOnlyList<CutoffWarning> Items, int Total);
