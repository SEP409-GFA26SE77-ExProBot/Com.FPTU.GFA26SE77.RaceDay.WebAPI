using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRunnerOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BibStatus",
                schema: "event",
                table: "event_registrations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Normal");

            migrationBuilder.AddColumn<string>(
                name: "RaceStatus",
                schema: "event",
                table: "event_registrations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotStarted");

            migrationBuilder.AddColumn<long>(
                name: "Version",
                schema: "event",
                table: "event_registrations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Preserve historical times/results and split the old combined lifecycle.
            migrationBuilder.Sql("""
                UPDATE event.event_registrations
                SET "RaceStatus" = CASE
                    WHEN "Status" = 'Finished' THEN 'Finished'
                    WHEN "StartedAtUtc" IS NOT NULL THEN 'Started'
                    ELSE 'NotStarted' END,
                    "BibStatus" = 'Normal';
                UPDATE event.event_registrations SET "Status" = 'Confirmed' WHERE "Status" = 'Finished';
                """);

            migrationBuilder.CreateTable(
                name: "runner_operations",
                schema: "event",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestedStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PreviousStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NewStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedVersion = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    BibNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RaceStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BibStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runner_operations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_runner_operations_event_registrations_RegistrationId",
                        column: x => x.RegistrationId,
                        principalSchema: "event",
                        principalTable: "event_registrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_runner_operations_RegistrationId_RequestId",
                schema: "event",
                table: "runner_operations",
                columns: new[] { "RegistrationId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_runner_operations_RegistrationId_Version",
                schema: "event",
                table: "runner_operations",
                columns: new[] { "RegistrationId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Do not silently lose operational audit or DNS/DNF/DQ decisions on rollback.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM event.runner_operations) THEN
                        RAISE EXCEPTION 'Cannot roll back runner operations while audit records exist. Export and plan data recovery first.';
                    END IF;
                END $$;
                UPDATE event.event_registrations SET "Status" = 'Finished'
                WHERE "Status" = 'Confirmed' AND "RaceStatus" = 'Finished';
                """);
            migrationBuilder.DropTable(
                name: "runner_operations",
                schema: "event");

            migrationBuilder.DropColumn(
                name: "BibStatus",
                schema: "event",
                table: "event_registrations");

            migrationBuilder.DropColumn(
                name: "RaceStatus",
                schema: "event",
                table: "event_registrations");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "event",
                table: "event_registrations");
        }
    }
}
