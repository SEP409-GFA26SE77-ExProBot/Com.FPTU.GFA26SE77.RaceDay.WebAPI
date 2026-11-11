using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSimulationDecisionSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CheckpointExternalId",
                schema: "event",
                table: "event_registration_race_status_changes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SimulationRunId",
                schema: "event",
                table: "event_registration_race_status_changes",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CheckpointExternalId",
                schema: "event",
                table: "event_registration_race_status_changes");

            migrationBuilder.DropColumn(
                name: "SimulationRunId",
                schema: "event",
                table: "event_registration_race_status_changes");
        }
    }
}
