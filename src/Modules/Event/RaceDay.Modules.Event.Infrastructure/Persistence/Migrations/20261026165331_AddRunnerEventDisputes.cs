using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRunnerEventDisputes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event_disputes",
                schema: "event",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_disputes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_disputes_race_events_EventId",
                        column: x => x.EventId,
                        principalSchema: "event",
                        principalTable: "race_events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_event_disputes_EventId",
                schema: "event",
                table: "event_disputes",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_event_disputes_RunnerUserId_CreatedAtUtc",
                schema: "event",
                table: "event_disputes",
                columns: new[] { "RunnerUserId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_disputes",
                schema: "event");
        }
    }
}
