using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Migrations;

[DbContext(typeof(EventDbContext))]
[Migration("20261116010000_AddSimulationJournal")]
public sealed class AddSimulationJournal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE event.simulation_journal (
            "Sequence" bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            "Id" uuid NOT NULL UNIQUE,
            "RunId" uuid NOT NULL,
            "EventId" uuid NOT NULL,
            "DistanceId" uuid NOT NULL,
            "CheckpointId" text NOT NULL,
            "OperationId" uuid NOT NULL UNIQUE,
            "Payload" jsonb NOT NULL
        );
        CREATE INDEX "IX_simulation_journal_scope" ON event.simulation_journal
            ("EventId", "DistanceId", "CheckpointId", "Sequence" DESC);
        CREATE INDEX "IX_simulation_journal_run" ON event.simulation_journal
            ("RunId", "CheckpointId", "Sequence" DESC);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE event.simulation_journal;");
}
