using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCutoffWarnings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cutoff_warnings",
                schema: "race_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DistanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DistanceName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BibNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RunnerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CheckpointId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckpointName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CheckpointKm = table.Column<double>(type: "double precision", nullable: false),
                    CheckpointSortOrder = table.Column<int>(type: "integer", nullable: false),
                    CutoffAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastCheckpointId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastCheckpointName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LastPassedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DetectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Resolution = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ResolvedByScanId = table.Column<Guid>(type: "uuid", nullable: true),
                    DismissedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DismissedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DismissalNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cutoff_warnings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cutoff_warnings_EventId_Status_DetectedAtUtc",
                schema: "race_monitoring",
                table: "cutoff_warnings",
                columns: new[] { "EventId", "Status", "DetectedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_cutoff_warnings_RegistrationId_CheckpointId",
                schema: "race_monitoring",
                table: "cutoff_warnings",
                columns: new[] { "RegistrationId", "CheckpointId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cutoff_warnings_RegistrationId_Status",
                schema: "race_monitoring",
                table: "cutoff_warnings",
                columns: new[] { "RegistrationId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cutoff_warnings",
                schema: "race_monitoring");
        }
    }
}
