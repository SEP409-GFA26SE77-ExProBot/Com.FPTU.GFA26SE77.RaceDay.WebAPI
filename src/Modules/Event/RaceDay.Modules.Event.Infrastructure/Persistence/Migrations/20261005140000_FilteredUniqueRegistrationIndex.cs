using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class FilteredUniqueRegistrationIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_event_registrations_EventId_DistanceId_UserId",
            schema: "event",
            table: "event_registrations");

        migrationBuilder.CreateIndex(
            name: "IX_event_registrations_EventId_DistanceId_UserId",
            schema: "event",
            table: "event_registrations",
            columns: new[] { "EventId", "DistanceId", "UserId" },
            unique: true,
            filter: "\"Status\" NOT IN ('Rejected', 'Cancelled')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_event_registrations_EventId_DistanceId_UserId",
            schema: "event",
            table: "event_registrations");

        migrationBuilder.CreateIndex(
            name: "IX_event_registrations_EventId_DistanceId_UserId",
            schema: "event",
            table: "event_registrations",
            columns: new[] { "EventId", "DistanceId", "UserId" },
            unique: true);
    }
}
