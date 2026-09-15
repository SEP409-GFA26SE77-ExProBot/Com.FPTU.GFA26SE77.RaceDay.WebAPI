using RaceDay.Shared.Domain.Abstractions;

namespace RaceDay.Modules.AuditLog.Domain.AuditEntries;

public sealed class AuditEntry : AggregateRoot
{
    public string Action { get; private set; } = string.Empty;
    public string ActorId { get; private set; } = string.Empty;
    public string ResourceType { get; private set; } = string.Empty;
    public string ResourceId { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = "{}";
    public DateTime OccurredOnUtc { get; private set; }

    private AuditEntry()
    {
    }

    public static AuditEntry Create(
        string action,
        string actorId,
        string resourceType,
        string resourceId,
        string payloadJson)
    {
        return new AuditEntry
        {
            Action = action,
            ActorId = actorId,
            ResourceType = resourceType,
            ResourceId = resourceId,
            PayloadJson = payloadJson,
            OccurredOnUtc = DateTime.UtcNow
        };
    }

    public static AuditEntry Restore(
        Guid id,
        string action,
        string actorId,
        string resourceType,
        string resourceId,
        string payloadJson,
        DateTime occurredOnUtc)
    {
        return new AuditEntry
        {
            Id = id,
            Action = action,
            ActorId = actorId,
            ResourceType = resourceType,
            ResourceId = resourceId,
            PayloadJson = payloadJson,
            OccurredOnUtc = occurredOnUtc
        };
    }
}
