using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialRaceMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "race_monitoring");

            migrationBuilder.CreateTable(
                name: "checkpoint_scans",
                schema: "race_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckpointId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScannedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ScanMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_checkpoint_scans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "runner_locations",
                schema: "race_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runner_locations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sos_alerts",
                schema: "race_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunnerLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    TriggeredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
                name: "IX_checkpoint_scans_CheckpointId_ScannedAtUtc",
                schema: "race_monitoring",
                table: "checkpoint_scans",
                columns: new[] { "CheckpointId", "ScannedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_runner_locations_RegistrationId_RecordedAtUtc",
                schema: "race_monitoring",
                table: "runner_locations",
                columns: new[] { "RegistrationId", "RecordedAtUtc" });

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "checkpoint_scans",
                schema: "race_monitoring");

            migrationBuilder.DropTable(
                name: "sos_alerts",
                schema: "race_monitoring");

            migrationBuilder.DropTable(
                name: "runner_locations",
                schema: "race_monitoring");
        }
    }
}
