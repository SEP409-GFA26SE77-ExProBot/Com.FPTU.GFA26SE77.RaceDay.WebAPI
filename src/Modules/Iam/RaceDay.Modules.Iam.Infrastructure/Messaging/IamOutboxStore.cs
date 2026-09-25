using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Iam.Infrastructure.Persistence;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Infrastructure.Messaging.Outbox;

namespace RaceDay.Modules.Iam.Infrastructure.Messaging;

public sealed class IamOutboxStore(IamDbContext db) : IOutboxStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task AddAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var type = integrationEvent.GetType();
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = integrationEvent.EventId == Guid.Empty ? Guid.NewGuid() : integrationEvent.EventId,
            EventType = type.FullName ?? type.Name,
            Payload = JsonSerializer.Serialize(integrationEvent, type, JsonOptions),
            OccurredOnUtc = integrationEvent.OccurredOnUtc,
            CreatedOnUtc = DateTime.UtcNow
        });

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<OutboxMessageData>> GetPendingAsync(int take, CancellationToken cancellationToken = default)
    {
        var rows = await db.OutboxMessages
            .Where(x => x.ProcessedOnUtc == null)
            .OrderBy(x => x.CreatedOnUtc)
            .Take(take)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new OutboxMessageData(x.Id, x.EventType, x.Payload, x.OccurredOnUtc, x.CreatedOnUtc))
            .ToList();
    }

    public async Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.OutboxMessages.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.ProcessedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
