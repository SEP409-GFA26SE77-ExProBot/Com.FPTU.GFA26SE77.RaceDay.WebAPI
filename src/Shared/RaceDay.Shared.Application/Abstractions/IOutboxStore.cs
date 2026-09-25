namespace RaceDay.Shared.Application.Abstractions;

public interface IOutboxStore
{
    Task AddAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OutboxMessageData>> GetPendingAsync(int take, CancellationToken cancellationToken = default);

    Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record OutboxMessageData(
    Guid Id,
    string EventType,
    string Payload,
    DateTime OccurredOnUtc,
    DateTime CreatedOnUtc);
