using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRfidTagAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rfid_tag_assignments",
                schema: "race_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagScheme = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TagId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rfid_tag_assignments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_rfid_tag_assignments_Event_Registration",
                schema: "race_monitoring",
                table: "rfid_tag_assignments",
                columns: new[] { "EventId", "RegistrationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rfid_tag_assignments_Event_Tag",
                schema: "race_monitoring",
                table: "rfid_tag_assignments",
                columns: new[] { "EventId", "TagScheme", "TagId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rfid_tag_assignments",
                schema: "race_monitoring");
        }
    }
}
