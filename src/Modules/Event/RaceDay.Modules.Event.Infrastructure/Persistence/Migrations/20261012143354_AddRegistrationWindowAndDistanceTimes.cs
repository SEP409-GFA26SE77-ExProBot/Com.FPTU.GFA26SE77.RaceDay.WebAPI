using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationWindowAndDistanceTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RegistrationClosesAtUtc",
                schema: "event",
                table: "race_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RegistrationOpensAtUtc",
                schema: "event",
                table: "race_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "EndAt",
                schema: "event",
                table: "event_distances",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "StartAt",
                schema: "event",
                table: "event_distances",
                type: "time without time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RegistrationClosesAtUtc",
                schema: "event",
                table: "race_events");

            migrationBuilder.DropColumn(
                name: "RegistrationOpensAtUtc",
                schema: "event",
                table: "race_events");

            migrationBuilder.DropColumn(
                name: "EndAt",
                schema: "event",
                table: "event_distances");

            migrationBuilder.DropColumn(
                name: "StartAt",
                schema: "event",
                table: "event_distances");
        }
    }
}
