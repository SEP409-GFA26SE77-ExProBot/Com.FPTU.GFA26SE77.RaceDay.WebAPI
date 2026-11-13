using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using RaceDay.Modules.Event.Contracts.Simulation;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Simulation;

public sealed class EfCheckpointResultExportStore(EventDbContext db) : ICheckpointResultExportStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CheckpointResultExport> SubmitAsync(CheckpointResultExport export, CancellationToken cancellationToken = default)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO event.simulation_checkpoint_exports
                    ("RunId", "EventId", "DistanceId", "CheckpointId", "SubmittedAtUtc", "Payload")
                VALUES (@run, @event, @distance, @checkpoint, @submitted, @payload)
                ON CONFLICT ("RunId", "CheckpointId") DO NOTHING
                """, (NpgsqlConnection)db.Database.GetDbConnection());
            command.Parameters.AddWithValue("run", export.RunId);
            command.Parameters.AddWithValue("event", export.EventId);
            command.Parameters.AddWithValue("distance", export.DistanceId);
            command.Parameters.AddWithValue("checkpoint", export.CheckpointId);
            command.Parameters.AddWithValue("submitted", export.SubmittedAtUtc);
            command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb)
                { Value = JsonSerializer.Serialize(export, JsonOptions) });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return (await GetAsync(export.RunId, export.CheckpointId, cancellationToken))!;
    }

    public async Task<CheckpointResultExport?> GetAsync(Guid runId, string checkpointId, CancellationToken cancellationToken = default)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT "Payload"::text FROM event.simulation_checkpoint_exports
                WHERE "RunId" = @run AND "CheckpointId" = @checkpoint
                """, (NpgsqlConnection)db.Database.GetDbConnection());
            command.Parameters.AddWithValue("run", runId);
            command.Parameters.AddWithValue("checkpoint", checkpointId);
            var json = await command.ExecuteScalarAsync(cancellationToken) as string;
            return json is null ? null : JsonSerializer.Deserialize<CheckpointResultExport>(json, JsonOptions);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    public async Task<IReadOnlyList<CheckpointResultExport>> ListAsync(Guid eventId, Guid distanceId, CancellationToken cancellationToken = default)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT "Payload"::text FROM event.simulation_checkpoint_exports
                WHERE "EventId" = @event AND "DistanceId" = @distance
                ORDER BY "SubmittedAtUtc" DESC
                """, (NpgsqlConnection)db.Database.GetDbConnection());
            command.Parameters.AddWithValue("event", eventId);
            command.Parameters.AddWithValue("distance", distanceId);
            var items = new List<CheckpointResultExport>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                items.Add(JsonSerializer.Deserialize<CheckpointResultExport>(reader.GetString(0), JsonOptions)!);
            return items;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
