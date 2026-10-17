using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RaceDay.Modules.Event.Infrastructure.Persistence;

#nullable disable

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(EventDbContext))]
    [Migration("20261015120000_AddRegistrationFinishTiming")]
    public partial class AddRegistrationFinishTiming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: DB đã có cột/bảng từ lần migrate trước / migration trùng.
            migrationBuilder.Sql(
                """
                ALTER TABLE event.event_registrations
                    ADD COLUMN IF NOT EXISTS "FinishedAtUtc" timestamp with time zone NULL;

                ALTER TABLE event.event_registrations
                    ADD COLUMN IF NOT EXISTS "ResultSeconds" bigint NULL;

                CREATE TABLE IF NOT EXISTS event.event_registration_checkpoint_passages
                (
                    "Id" uuid NOT NULL,
                    "RegistrationId" uuid NOT NULL,
                    "EventId" uuid NOT NULL,
                    "DistanceId" uuid NOT NULL,
                    "CheckpointId" uuid NOT NULL,
                    "PassedAtUtc" timestamp with time zone NOT NULL,
                    "CreatedAtUtc" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_event_registration_checkpoint_passages" PRIMARY KEY ("Id")
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "IX_event_registration_checkpoint_passages_RegistrationId_CheckpointId"
                    ON event.event_registration_checkpoint_passages ("RegistrationId", "CheckpointId");

                CREATE INDEX IF NOT EXISTS "IX_event_registration_checkpoint_passages_EventId"
                    ON event.event_registration_checkpoint_passages ("EventId");

                CREATE INDEX IF NOT EXISTS "IX_event_registration_checkpoint_passages_DistanceId"
                    ON event.event_registration_checkpoint_passages ("DistanceId");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TABLE IF EXISTS event.event_registration_checkpoint_passages;

                ALTER TABLE event.event_registrations
                    DROP COLUMN IF EXISTS "FinishedAtUtc";

                ALTER TABLE event.event_registrations
                    DROP COLUMN IF EXISTS "ResultSeconds";
                """);
        }
    }
}
