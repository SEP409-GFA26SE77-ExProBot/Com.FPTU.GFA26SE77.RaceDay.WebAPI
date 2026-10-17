using System;
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
            // 1. Thêm 2 cột timing vào bảng event_registrations
            migrationBuilder.AddColumn<DateTime>(
                name: "FinishedAtUtc",
                schema: "event",
                table: "event_registrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ResultSeconds",
                schema: "event",
                table: "event_registrations",
                type: "bigint",
                nullable: true);

            // 2. Tạo bảng với đúng tên: event_registration_checkpoint_passages
            migrationBuilder.CreateTable(
                name: "event_registration_checkpoint_passages",
                schema: "event",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    DistanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckpointId = table.Column<Guid>(type: "uuid", nullable: false),
                    PassedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_registration_checkpoint_passages", x => x.Id);
                });

            // 3. Tạo Indexes khớp với DbContext
            migrationBuilder.CreateIndex(
                name: "IX_event_registration_checkpoint_passages_RegistrationId_CheckpointId",
                schema: "event",
                table: "event_registration_checkpoint_passages",
                columns: new[] { "RegistrationId", "CheckpointId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_event_registration_checkpoint_passages_EventId",
                schema: "event",
                table: "event_registration_checkpoint_passages",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_event_registration_checkpoint_passages_DistanceId",
                schema: "event",
                table: "event_registration_checkpoint_passages",
                column: "DistanceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback bảng trước
            migrationBuilder.DropTable(
                name: "event_registration_checkpoint_passages",
                schema: "event");

            // Rollback các cột sau
            migrationBuilder.DropColumn(
                name: "FinishedAtUtc",
                schema: "event",
                table: "event_registrations");

            migrationBuilder.DropColumn(
                name: "ResultSeconds",
                schema: "event",
                table: "event_registrations");
        }
    }
}