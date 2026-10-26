namespace RaceDay.Modules.Event.Contracts.Events;

/// <summary>Cross-module: confirmed BIB roster snapshot for OCR / photo tagging.</summary>
public interface IEventBibRosterQuery
{
    Task<EventBibRosterSnapshot> GetConfirmedBibsAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);
}

public sealed record EventBibRosterSnapshot(
    Guid EventId,
    string Version,
    IReadOnlyList<string> Bibs);
