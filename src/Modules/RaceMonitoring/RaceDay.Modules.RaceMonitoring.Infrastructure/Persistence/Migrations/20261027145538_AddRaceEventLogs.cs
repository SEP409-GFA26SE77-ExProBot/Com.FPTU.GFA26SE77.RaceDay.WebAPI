using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRaceEventLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "race_event_logs",
                schema: "race_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventDistanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BibNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CheckpointId = table.Column<Guid>(type: "uuid", nullable: true),
                    RegistrationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_race_event_logs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_race_event_logs_EventId_BibNumber",
                schema: "race_monitoring",
                table: "race_event_logs",
                columns: new[] { "EventId", "BibNumber" },
                filter: "\"BibNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_race_event_logs_EventId_Distance_Sequence",
                schema: "race_monitoring",
                table: "race_event_logs",
                columns: new[] { "EventId", "EventDistanceId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_race_event_logs_EventId_OccurredAtUtc_Sequence",
                schema: "race_monitoring",
                table: "race_event_logs",
                columns: new[] { "EventId", "OccurredAtUtc", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_race_event_logs_EventId_Sequence",
                schema: "race_monitoring",
                table: "race_event_logs",
                columns: new[] { "EventId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_race_event_logs_RaceStarted_Distance",
                schema: "race_monitoring",
                table: "race_event_logs",
                columns: new[] { "EventId", "EventDistanceId" },
                unique: true,
                filter: "\"EventType\" = 'RaceStarted'");

            migrationBuilder.CreateIndex(
                name: "IX_race_event_logs_Source",
                schema: "race_monitoring",
                table: "race_event_logs",
                columns: new[] { "SourceType", "SourceId" },
                unique: true,
                filter: "\"SourceId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "race_event_logs",
                schema: "race_monitoring");
        }
    }
}
