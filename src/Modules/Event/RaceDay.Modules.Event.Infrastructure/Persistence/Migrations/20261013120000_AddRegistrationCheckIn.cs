using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RaceDay.Modules.Event.Infrastructure.Persistence;

#nullable disable

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(EventDbContext))]
    [Migration("20261013120000_AddRegistrationCheckIn")]
    public partial class AddRegistrationCheckIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CheckedInAtUtc",
                schema: "event",
                table: "event_registrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CheckedInByUserId",
                schema: "event",
                table: "event_registrations",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CheckedInAtUtc",
                schema: "event",
                table: "event_registrations");

            migrationBuilder.DropColumn(
                name: "CheckedInByUserId",
                schema: "event",
                table: "event_registrations");
        }
    }
}
