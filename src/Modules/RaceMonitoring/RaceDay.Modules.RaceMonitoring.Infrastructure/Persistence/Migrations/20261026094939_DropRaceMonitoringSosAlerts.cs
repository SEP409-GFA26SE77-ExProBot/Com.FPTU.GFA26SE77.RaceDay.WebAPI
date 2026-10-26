using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropRaceMonitoringSosAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_emergency_responses_sos_alerts_SOSAlertId",
                schema: "race_monitoring",
                table: "emergency_responses");

            migrationBuilder.DropTable(
                name: "sos_alerts",
                schema: "race_monitoring");

            migrationBuilder.RenameColumn(
                name: "SOSAlertId",
                schema: "race_monitoring",
                table: "emergency_responses",
                newName: "SourceSosAlertId");

            migrationBuilder.RenameIndex(
                name: "IX_emergency_responses_SOSAlertId",
                schema: "race_monitoring",
                table: "emergency_responses",
                newName: "IX_emergency_responses_SourceSosAlertId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SourceSosAlertId",
                schema: "race_monitoring",
                table: "emergency_responses",
                newName: "SOSAlertId");

            migrationBuilder.RenameIndex(
                name: "IX_emergency_responses_SourceSosAlertId",
                schema: "race_monitoring",
                table: "emergency_responses",
                newName: "IX_emergency_responses_SOSAlertId");

            migrationBuilder.CreateTable(
                name: "sos_alerts",
                schema: "race_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunnerLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    RegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TriggeredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sos_alerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sos_alerts_runner_locations_RunnerLocationId",
                        column: x => x.RunnerLocationId,
                        principalSchema: "race_monitoring",
                        principalTable: "runner_locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sos_alerts_RunnerLocationId",
                schema: "race_monitoring",
                table: "sos_alerts",
                column: "RunnerLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_sos_alerts_Status_TriggeredAtUtc",
                schema: "race_monitoring",
                table: "sos_alerts",
                columns: new[] { "Status", "TriggeredAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_emergency_responses_sos_alerts_SOSAlertId",
                schema: "race_monitoring",
                table: "emergency_responses",
                column: "SOSAlertId",
                principalSchema: "race_monitoring",
                principalTable: "sos_alerts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
