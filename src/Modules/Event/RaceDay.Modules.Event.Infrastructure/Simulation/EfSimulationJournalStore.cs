using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using RaceDay.Modules.Event.Contracts.Simulation;
using RaceDay.Modules.Event.Infrastructure.Persistence;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Infrastructure.Simulation;

public sealed class EfSimulationJournalStore(EventDbContext db) : ISimulationJournalStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private NpgsqlConnection Connection() => new(db.Database.GetConnectionString());

    public async Task<SimulationJournalEntry?> FindOperationAsync(Guid operationId, CancellationToken ct = default)
    {
        await using var connection = Connection();
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT \"Sequence\", \"Payload\"::text FROM event.simulation_journal WHERE \"OperationId\" = @operation", connection);
        command.Parameters.AddWithValue("operation", operationId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Entry(reader) : null;
    }

    public async Task<SimulationJournalEntry> AppendAsync(SimulationJournalEntry entry, CancellationToken ct = default)
    {
        await using var connection = Connection();
        await connection.OpenAsync(ct);
        return await InsertAsync(connection, null, entry, ct);
    }

    private static async Task<SimulationJournalEntry> InsertAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, SimulationJournalEntry entry, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO event.simulation_journal ("Id", "RunId", "EventId", "DistanceId", "CheckpointId", "OperationId", "Payload")
            VALUES (@id, @run, @event, @distance, @checkpoint, @operation, @payload)
            RETURNING "Sequence"
            """, connection, transaction);
        command.Parameters.AddWithValue("id", entry.Id);
        command.Parameters.AddWithValue("run", entry.RunId);
        command.Parameters.AddWithValue("event", entry.EventId);
        command.Parameters.AddWithValue("distance", entry.DistanceId);
        command.Parameters.AddWithValue("checkpoint", entry.CheckpointId);
        command.Parameters.AddWithValue("operation", entry.OperationId);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(entry, Json));
        return entry with { Sequence = (long)(await command.ExecuteScalarAsync(ct))! };
    }

    public async Task<SimulationJournalPage> ReadAsync(SimulationJournalQuery query, CancellationToken ct = default)
    {
        if (query.Cursor is <= 0 || query.Search?.Length > 200 || query.Kind?.Length > 40)
            throw new DomainException("Bộ lọc nhật ký không hợp lệ.");
        await using var connection = Connection();
        await connection.OpenAsync(ct);
        await using var command = Scope(connection, query.EventId, query.DistanceId, query.CheckpointId);
        command.CommandText = "SELECT \"Sequence\", \"Payload\"::text FROM event.simulation_journal WHERE " + command.CommandText;
        command.CommandText += " AND \"RunId\" = @run";
        command.Parameters.AddWithValue("run", query.RunId);
        if (!string.IsNullOrWhiteSpace(query.Kind))
        {
            command.CommandText += " AND \"Payload\"->>'kind' = @kind";
            command.Parameters.AddWithValue("kind", query.Kind);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            command.CommandText += " AND strpos(lower(concat(\"Payload\"->>'bib', ' ', \"Payload\"->>'fullName')), lower(@search)) > 0";
            command.Parameters.AddWithValue("search", query.Search.Trim());
        }
        if (query.ActorId is Guid actor)
        {
            command.CommandText += " AND \"Payload\"->>'actorId' = @actor";
            command.Parameters.AddWithValue("actor", actor.ToString());
        }
        if (query.Cursor is long cursor)
        {
            command.CommandText += " AND \"Sequence\" < @cursor";
            command.Parameters.AddWithValue("cursor", cursor);
        }
        command.CommandText += " ORDER BY \"Sequence\" DESC LIMIT 26";
        var entries = new List<SimulationJournalEntry>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) entries.Add(Entry(reader));
        return new(entries.Take(25).ToArray(), entries.Count > 25 ? entries[24].Sequence : null);
    }

    public async Task<IReadOnlyList<SimulationJournalRun>> RunsAsync(Guid eventId, Guid distanceId, string checkpointId, CancellationToken ct = default)
    {
        await using var connection = Connection();
        await connection.OpenAsync(ct);
        await using var command = Scope(connection, eventId, distanceId, checkpointId);
        command.CommandText = "SELECT \"RunId\", max((\"Payload\"->>'occurredAtUtc')::timestamptz) AS last FROM event.simulation_journal WHERE "
            + command.CommandText + " GROUP BY \"RunId\" ORDER BY last DESC";
        var runs = new List<SimulationJournalRun>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) runs.Add(new(reader.GetGuid(0), reader.GetDateTime(1)));
        return runs;
    }

    public async Task<long> VersionAsync(Guid eventId, Guid distanceId, string checkpointId, CancellationToken ct = default)
    {
        await using var connection = Connection();
        await connection.OpenAsync(ct);
        await using var command = Scope(connection, eventId, distanceId, checkpointId);
        command.CommandText = "SELECT coalesce(max(\"Sequence\"), 0) FROM event.simulation_journal WHERE " + command.CommandText;
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    public Task<CheckpointResultExport?> GetExportAsync(Guid runId, string checkpointId, CancellationToken ct = default) =>
        new EfCheckpointResultExportStore(db).GetAsync(runId, checkpointId, ct);

    public async Task<CheckpointResultExport> SubmitExportAsync(CheckpointResultExport export, SimulationJournalEntry entry, CancellationToken ct = default)
    {
        await using var connection = Connection();
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand("""
            INSERT INTO event.simulation_checkpoint_exports
                ("RunId", "EventId", "DistanceId", "CheckpointId", "SubmittedAtUtc", "Payload")
            VALUES (@run, @event, @distance, @checkpoint, @submitted, @payload)
            ON CONFLICT ("RunId", "CheckpointId") DO NOTHING
            """, connection, transaction);
        command.Parameters.AddWithValue("run", export.RunId);
        command.Parameters.AddWithValue("event", export.EventId);
        command.Parameters.AddWithValue("distance", export.DistanceId);
        command.Parameters.AddWithValue("checkpoint", export.CheckpointId);
        command.Parameters.AddWithValue("submitted", export.SubmittedAtUtc);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(export, Json));
        if (await command.ExecuteNonQueryAsync(ct) == 1) await InsertAsync(connection, transaction, entry, ct);
        await transaction.CommitAsync(ct);
        return (await GetExportAsync(export.RunId, export.CheckpointId, ct))!;
    }

    private static SimulationJournalEntry Entry(NpgsqlDataReader reader) =>
        JsonSerializer.Deserialize<SimulationJournalEntry>(reader.GetString(1), Json)! with { Sequence = reader.GetInt64(0) };

    private static NpgsqlCommand Scope(NpgsqlConnection connection, Guid eventId, Guid distanceId, string checkpointId)
    {
        var command = new NpgsqlCommand("\"EventId\" = @event AND \"DistanceId\" = @distance AND \"CheckpointId\" = @checkpoint", connection);
        command.Parameters.AddWithValue("event", eventId);
        command.Parameters.AddWithValue("distance", distanceId);
        command.Parameters.AddWithValue("checkpoint", checkpointId);
        return command;
    }
}
