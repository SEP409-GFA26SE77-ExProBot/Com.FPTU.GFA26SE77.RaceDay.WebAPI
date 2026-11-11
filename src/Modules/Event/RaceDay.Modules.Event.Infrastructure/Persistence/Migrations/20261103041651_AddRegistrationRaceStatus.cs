using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationRaceStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RaceStatus",
                schema: "event",
                table: "event_registrations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotStarted");

            migrationBuilder.Sql("""
                UPDATE "event"."event_registrations"
                SET "RaceStatus" = CASE
                    WHEN "Status" = 'Finished' OR "FinishedAtUtc" IS NOT NULL THEN 'Finished'
                    WHEN "StartedAtUtc" IS NOT NULL THEN 'Running'
                    ELSE 'NotStarted'
                END;
                """);

            migrationBuilder.CreateTable(
                name: "event_registration_race_status_changes",
                schema: "event",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_registration_race_status_changes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_registration_race_status_changes_event_registrations_~",
                        column: x => x.RegistrationId,
                        principalSchema: "event",
                        principalTable: "event_registrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_event_registration_race_status_changes_RegistrationId_Chang~",
                schema: "event",
                table: "event_registration_race_status_changes",
                columns: new[] { "RegistrationId", "ChangedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_registration_race_status_changes",
                schema: "event");

            migrationBuilder.DropColumn(
                name: "RaceStatus",
                schema: "event",
                table: "event_registrations");
        }
    }
}
