using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixPendingModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event_registration_checkpoint_passages",
                schema: "event",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    DistanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckpointId = table.Column<Guid>(type: "uuid", nullable: false),
                    PassedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_registration_checkpoint_passages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_event_registration_checkpoint_passages_DistanceId",
                schema: "event",
                table: "event_registration_checkpoint_passages",
                column: "DistanceId");

            migrationBuilder.CreateIndex(
                name: "IX_event_registration_checkpoint_passages_EventId",
                schema: "event",
                table: "event_registration_checkpoint_passages",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_event_registration_checkpoint_passages_RegistrationId_Check~",
                schema: "event",
                table: "event_registration_checkpoint_passages",
                columns: new[] { "RegistrationId", "CheckpointId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_registration_checkpoint_passages",
                schema: "event");
        }
    }
}
