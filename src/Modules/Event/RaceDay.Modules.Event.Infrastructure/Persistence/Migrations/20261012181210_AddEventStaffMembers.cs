using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEventStaffMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event_staff_members",
                schema: "event",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FullName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    StationId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    StationLabel = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_staff_members", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_event_staff_members_EventId",
                schema: "event",
                table: "event_staff_members",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_event_staff_members_EventId_Role_Email",
                schema: "event",
                table: "event_staff_members",
                columns: new[] { "EventId", "Role", "Email" },
                unique: true,
                filter: "\"Status\" <> 'Revoked'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_staff_members",
                schema: "event");
        }
    }
}
