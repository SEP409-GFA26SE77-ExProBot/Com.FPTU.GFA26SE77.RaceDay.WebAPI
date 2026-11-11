using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Migrations;

[DbContext(typeof(EventDbContext))]
[Migration("20261111050000_AddSimulationCheckpointExports")]
public sealed class AddSimulationCheckpointExports : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE event.simulation_checkpoint_exports (
                "RunId" uuid NOT NULL,
                "EventId" uuid NOT NULL,
                "DistanceId" uuid NOT NULL,
                "CheckpointId" text NOT NULL,
                "SubmittedAtUtc" timestamp with time zone NOT NULL,
                "Payload" jsonb NOT NULL,
                CONSTRAINT "PK_simulation_checkpoint_exports" PRIMARY KEY ("RunId", "CheckpointId")
            );
            CREATE INDEX "IX_simulation_checkpoint_exports_Event_Distance"
                ON event.simulation_checkpoint_exports ("EventId", "DistanceId", "SubmittedAtUtc" DESC);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE event.simulation_checkpoint_exports;");
    }
}
