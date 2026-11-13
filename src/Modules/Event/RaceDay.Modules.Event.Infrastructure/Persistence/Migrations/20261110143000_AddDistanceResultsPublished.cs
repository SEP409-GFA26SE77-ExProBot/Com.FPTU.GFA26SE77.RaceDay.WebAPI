using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RaceDay.Modules.Event.Infrastructure.Persistence;

#nullable disable

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(EventDbContext))]
[Migration("20261110143000_AddDistanceResultsPublished")]
public partial class AddDistanceResultsPublished : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Idempotent: cột có thể đã có từ lần apply thủ công / snapshot lệch history.
        migrationBuilder.Sql(
            """
            ALTER TABLE event.event_distances
                ADD COLUMN IF NOT EXISTS "ResultsPublished" boolean NOT NULL DEFAULT false;

            ALTER TABLE event.event_distances
                ADD COLUMN IF NOT EXISTS "ResultsPublishedAtUtc" timestamp with time zone NULL;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE event.event_distances DROP COLUMN IF EXISTS "ResultsPublishedAtUtc";
            ALTER TABLE event.event_distances DROP COLUMN IF EXISTS "ResultsPublished";
            """);
    }
}
