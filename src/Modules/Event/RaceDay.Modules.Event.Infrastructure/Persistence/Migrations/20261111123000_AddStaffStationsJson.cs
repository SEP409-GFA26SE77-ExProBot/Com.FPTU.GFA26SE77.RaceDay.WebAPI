using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RaceDay.Modules.Event.Infrastructure.Persistence;

#nullable disable

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(EventDbContext))]
[Migration("20261111123000_AddStaffStationsJson")]
public partial class AddStaffStationsJson : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE event.event_staff_members
                ADD COLUMN IF NOT EXISTS "StationsJson" jsonb NOT NULL DEFAULT '[]'::jsonb;

            -- Backfill từ StationId/StationLabel cũ (photographer/race-staff 1 điểm).
            UPDATE event.event_staff_members
            SET "StationsJson" = jsonb_build_array(
                jsonb_build_object(
                    'stationId', "StationId",
                    'stationLabel', "StationLabel"
                )
            )
            WHERE "StationId" IS NOT NULL
              AND TRIM("StationId") <> ''
              AND ("StationsJson" IS NULL OR "StationsJson" = '[]'::jsonb);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE event.event_staff_members DROP COLUMN IF EXISTS "StationsJson";
            """);
    }
}
