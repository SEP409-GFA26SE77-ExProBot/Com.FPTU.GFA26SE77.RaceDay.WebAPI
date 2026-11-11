using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddDistanceResultsPublished : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "ResultsPublished",
            schema: "event",
            table: "event_distances",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTime>(
            name: "ResultsPublishedAtUtc",
            schema: "event",
            table: "event_distances",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ResultsPublished",
            schema: "event",
            table: "event_distances");

        migrationBuilder.DropColumn(
            name: "ResultsPublishedAtUtc",
            schema: "event",
            table: "event_distances");
    }
}
