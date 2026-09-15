using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using RaceDay.Modules.AuditLog.Application.Abstractions;
using RaceDay.Modules.AuditLog.Domain.AuditEntries;
using RaceDay.Shared.Infrastructure.Persistence.Mongo;

namespace RaceDay.Modules.AuditLog.Infrastructure.Repositories;

public sealed class AuditEntryRepository(IMongoDbContext mongo) : IAuditEntryRepository
{
    private const string CollectionName = "audit_entries";

    public async Task AddAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        var document = AuditEntryDocument.FromDomain(entry);
        await mongo.GetCollection<AuditEntryDocument>(CollectionName)
            .InsertOneAsync(document, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEntry>> ListRecentAsync(int take, CancellationToken cancellationToken = default)
    {
        var docs = await mongo.GetCollection<AuditEntryDocument>(CollectionName)
            .Find(FilterDefinition<AuditEntryDocument>.Empty)
            .SortByDescending(x => x.OccurredOnUtc)
            .Limit(take)
            .ToListAsync(cancellationToken);

        return docs.Select(x => x.ToDomain()).ToList();
    }
}

internal sealed class AuditEntryDocument
{
    [BsonId]
    public Guid Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTime OccurredOnUtc { get; set; }

    public static AuditEntryDocument FromDomain(AuditEntry entry) => new()
    {
        Id = entry.Id,
        Action = entry.Action,
        ActorId = entry.ActorId,
        ResourceType = entry.ResourceType,
        ResourceId = entry.ResourceId,
        PayloadJson = entry.PayloadJson,
        OccurredOnUtc = entry.OccurredOnUtc
    };

    public AuditEntry ToDomain() => AuditEntry.Restore(
        Id, Action, ActorId, ResourceType, ResourceId, PayloadJson, OccurredOnUtc);
}
