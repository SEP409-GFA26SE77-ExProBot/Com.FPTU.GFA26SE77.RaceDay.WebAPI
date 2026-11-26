using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using System.Text.Json;
using RaceDay.Modules.Event.Contracts.Simulation;
using RaceDay.Modules.Event.Infrastructure.Persistence;
using RaceDay.Modules.Event.Infrastructure.Persistence.Migrations;
using RaceDay.Modules.Event.Infrastructure.Simulation;

namespace RaceDay.Modules.Simulation.UnitTests;

public sealed class JournalDatabaseFactAttribute : FactAttribute
{
    public JournalDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STAFF_JOURNAL_TEST_POSTGRES")))
            Skip = "Set STAFF_JOURNAL_TEST_POSTGRES to run against an isolated temporary PostgreSQL database.";
    }
}

public class StaffJournalDatabaseTests
{
    [JournalDatabaseFact]
    public async Task Migration_preserves_legacy_exports_and_journal_is_durable_scoped_paginated_and_atomic()
    {
        var adminString = Environment.GetEnvironmentVariable("STAFF_JOURNAL_TEST_POSTGRES")!;
        var database = "raceday_journal_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(adminString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            var testString = new NpgsqlConnectionStringBuilder(adminString) { Database = database, Pooling = false }.ConnectionString;
            var options = new DbContextOptionsBuilder<EventDbContext>().UseNpgsql(testString).Options;
            await using var db = new EventDbContext(options);
            await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA event");
            var generator = db.GetService<IMigrationsSqlGenerator>();
            foreach (var command in generator.Generate(new AddSimulationCheckpointExports().UpOperations))
                await db.Database.ExecuteSqlRawAsync(command.CommandText);
            var eventId = Guid.NewGuid();
            var distanceId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var actorId = Guid.NewGuid();
            var legacy = new CheckpointResultExport(runId, eventId, distanceId, "legacy", "Legacy", 1, actorId, DateTime.UtcNow, [], []);
            var oldStore = new EfCheckpointResultExportStore(db);
            await oldStore.SubmitAsync(legacy);
            foreach (var command in generator.Generate(new AddSimulationJournal().UpOperations))
                await db.Database.ExecuteSqlRawAsync(command.CommandText);
            var store = new EfSimulationJournalStore(db);
            Assert.Equal(JsonSerializer.Serialize(legacy), JsonSerializer.Serialize(await store.GetExportAsync(runId, "legacy")));
            Assert.Equal(0, await store.VersionAsync(eventId, distanceId, "legacy"));
            SimulationJournalEntry Entry(int i) => new(Guid.NewGuid(), 0, runId, eventId, distanceId, "cp", "DNF_RECORDED",
                actorId, "Staff A", "Race-staff", Guid.NewGuid(), i.ToString(), "Runner Name", DateTime.UtcNow, i,
                "Racing", "DNF", "Reason", null, Guid.NewGuid(), Guid.NewGuid(), "hash");
            var written = new List<SimulationJournalEntry>();
            for (var i = 0; i < 30; i++) written.Add(await store.AppendAsync(Entry(i)));
            var first = await store.ReadAsync(new(eventId, distanceId, "cp", runId));
            Assert.Equal(25, first.Items.Count);
            Assert.NotNull(first.NextCursor);
            var second = await store.ReadAsync(new(eventId, distanceId, "cp", runId, Cursor: first.NextCursor));
            Assert.Equal(5, second.Items.Count);
            Assert.Null(second.NextCursor);
            Assert.DoesNotContain(second.Items, e => first.Items.Any(f => f.Id == e.Id));
            Assert.Empty((await store.ReadAsync(new(Guid.NewGuid(), distanceId, "cp", runId))).Items);
            Assert.Empty((await store.ReadAsync(new(eventId, distanceId, "other", runId))).Items);
            Assert.Empty((await store.ReadAsync(new(eventId, distanceId, "cp", runId, ActorId: Guid.NewGuid()))).Items);
            Assert.Empty((await store.ReadAsync(new(eventId, distanceId, "cp", runId, Kind: "DQ_APPROVED"))).Items);
            Assert.Single((await store.ReadAsync(new(eventId, distanceId, "cp", runId, Search: "29"))).Items);
            Assert.Equal(25, (await store.ReadAsync(new(eventId, distanceId, "cp", runId, Search: "runner name"))).Items.Count);
            await using var restartedDb = new EventDbContext(options);
            var restarted = new EfSimulationJournalStore(restartedDb);
            Assert.Single(await restarted.RunsAsync(eventId, distanceId, "cp"));
            Assert.Equal(written.Last().Sequence, await restarted.VersionAsync(eventId, distanceId, "cp"));
            Assert.Equal(written[0], await restarted.FindOperationAsync(written[0].OperationId));

            var export = legacy with { CheckpointId = "cp", StaffName = "Staff A" };
            var exported = Entry(31) with { Kind = "CHECKPOINT_SUBMITTED", RegistrationId = null, Bib = null, FullName = null };
            await store.SubmitExportAsync(export, exported);
            await store.SubmitExportAsync(export, exported with { Id = Guid.NewGuid(), OperationId = Guid.NewGuid() });
            Assert.Single((await store.ReadAsync(new(eventId, distanceId, "cp", runId, Kind: "CHECKPOINT_SUBMITTED"))).Items);
            Assert.Equal(JsonSerializer.Serialize(export), JsonSerializer.Serialize(await restarted.GetExportAsync(runId, "cp")));
            // A journal insert failure rolls back the export in the same transaction.
            var failedExport = export with { CheckpointId = "rollback" };
            await Assert.ThrowsAsync<PostgresException>(() => store.SubmitExportAsync(failedExport,
                exported with { CheckpointId = "rollback", OperationId = Guid.NewGuid() }));
            Assert.Null(await restarted.GetExportAsync(runId, "rollback"));
            Assert.Empty((await restarted.ReadAsync(new(eventId, distanceId, "rollback", runId))).Items);
        }
        finally
        {
            // Only the unique database created above is ever removed.
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
