using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using RaceDay.Modules.Event.Infrastructure.Persistence.Migrations;

namespace RaceDay.Modules.Event.IntegrationTests;

public class RaceStatusMigrationTests
{
    [Fact]
    public async Task Backfill_UsesFinishThenStart_AndDefaultsUnstartedRows()
    {
        var migration = new AddRegistrationRaceStatus();
        var column = Assert.Single(migration.UpOperations.OfType<AddColumnOperation>());
        Assert.Equal("NotStarted", column.DefaultValue);
        var backfill = Assert.Single(migration.UpOperations.OfType<SqlOperation>()).Sql;

        // Execute the migration's actual CASE expression against representative legacy rows.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var setup = connection.CreateCommand();
        setup.CommandText = """
            ATTACH DATABASE ':memory:' AS "event";
            CREATE TABLE "event"."event_registrations" (
                "Id" INTEGER, "Status" TEXT, "StartedAtUtc" TEXT, "FinishedAtUtc" TEXT, "RaceStatus" TEXT
            );
            INSERT INTO "event"."event_registrations" VALUES
                (1, 'Confirmed', NULL, NULL, 'NotStarted'),
                (2, 'Confirmed', '2026-11-03T07:00:00Z', NULL, 'NotStarted'),
                (3, 'Finished', '2026-11-03T07:00:00Z', '2026-11-03T08:00:00Z', 'NotStarted'),
                (4, 'Finished', NULL, NULL, 'NotStarted'),
                (5, 'Confirmed', NULL, '2026-11-03T08:00:00Z', 'NotStarted'),
                (6, 'PendingPayment', NULL, NULL, 'NotStarted');
            """;
        await setup.ExecuteNonQueryAsync();
        using var update = connection.CreateCommand();
        update.CommandText = backfill;
        await update.ExecuteNonQueryAsync();
        using var query = connection.CreateCommand();
        query.CommandText = """SELECT "RaceStatus" FROM "event"."event_registrations" ORDER BY "Id";""";
        using var reader = await query.ExecuteReaderAsync();
        var results = new List<string>();
        while (await reader.ReadAsync()) results.Add(reader.GetString(0));
        Assert.Equal(["NotStarted", "Running", "Finished", "Finished", "Finished", "NotStarted"], results);
    }
}
