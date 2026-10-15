using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.Map.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "map");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "map",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "race_courses",
                schema: "map",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    DistanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DistanceKm = table.Column<double>(type: "double precision", precision: 10, scale: 3, nullable: false),
                    CourseJson = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_race_courses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_CreatedOnUtc",
                schema: "map",
                table: "outbox_messages",
                column: "CreatedOnUtc");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_ProcessedOnUtc",
                schema: "map",
                table: "outbox_messages",
                column: "ProcessedOnUtc");

            migrationBuilder.CreateIndex(
                name: "IX_race_courses_EventId",
                schema: "map",
                table: "race_courses",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_race_courses_EventId_DistanceId",
                schema: "map",
                table: "race_courses",
                columns: new[] { "EventId", "DistanceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "map");

            migrationBuilder.DropTable(
                name: "race_courses",
                schema: "map");
        }
    }
}
