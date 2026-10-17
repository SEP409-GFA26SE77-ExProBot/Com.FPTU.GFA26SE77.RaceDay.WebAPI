using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RaceDay.Modules.Event.Infrastructure.Persistence;

#nullable disable

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(EventDbContext))]
[Migration("20261016120000_AddEventBibConfig")]
public partial class AddEventBibConfig : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "BibPrefix",
            schema: "event",
            table: "race_events",
            type: "character varying(12)",
            maxLength: 12,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "BibRangeStart",
            schema: "event",
            table: "race_events",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "BibRangeEnd",
            schema: "event",
            table: "race_events",
            type: "integer",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "BibPrefix",
            schema: "event",
            table: "race_events");

        migrationBuilder.DropColumn(
            name: "BibRangeStart",
            schema: "event",
            table: "race_events");

        migrationBuilder.DropColumn(
            name: "BibRangeEnd",
            schema: "event",
            table: "race_events");
    }
}
