namespace RaceDay.Shared.Infrastructure.Messaging.Outbox;

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public DateTime OccurredOnUtc { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime? ProcessedOnUtc { get; set; }
}
