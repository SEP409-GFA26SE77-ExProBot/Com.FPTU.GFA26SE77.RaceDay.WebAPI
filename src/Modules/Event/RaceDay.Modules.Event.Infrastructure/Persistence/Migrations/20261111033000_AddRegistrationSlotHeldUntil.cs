using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RaceDay.Modules.Event.Infrastructure.Persistence;

#nullable disable

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(EventDbContext))]
[Migration("20261111033000_AddRegistrationSlotHeldUntil")]
public partial class AddRegistrationSlotHeldUntil : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE event.event_registrations
                ADD COLUMN IF NOT EXISTS "SlotHeldUntilUtc" timestamp with time zone NULL;

            CREATE INDEX IF NOT EXISTS "IX_event_registrations_Status_SlotHeldUntilUtc"
                ON event.event_registrations ("Status", "SlotHeldUntilUtc");
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS event."IX_event_registrations_Status_SlotHeldUntilUtc";
            ALTER TABLE event.event_registrations DROP COLUMN IF EXISTS "SlotHeldUntilUtc";
            """);
    }
}
