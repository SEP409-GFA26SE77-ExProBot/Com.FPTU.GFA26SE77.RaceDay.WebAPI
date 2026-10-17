using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.Device.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialWearSos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "device");

            // Clean leftover RFID tables from earlier scaffold (keep __EFMigrationsHistory).
            migrationBuilder.Sql(
                """
                DROP TABLE IF EXISTS device.device_scans CASCADE;
                DROP TABLE IF EXISTS device.race_devices CASCADE;
                """);

            migrationBuilder.CreateTable(
                name: "device_auth_requests",
                schema: "device",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientDeviceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DeviceCodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RunnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    Consumed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_auth_requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "linked_devices",
                schema: "device",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientDeviceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    RefreshTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LinkedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_linked_devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "device",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sos_alerts",
                schema: "device",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcknowledgedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sos_alerts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sos_locations",
                schema: "device",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SosAlertId = table.Column<Guid>(type: "uuid", nullable: false),
                    Lat = table.Column<double>(type: "double precision", nullable: false),
                    Lng = table.Column<double>(type: "double precision", nullable: false),
                    Accuracy = table.Column<double>(type: "double precision", nullable: true),
                    Battery = table.Column<int>(type: "integer", nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sos_locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sos_locations_sos_alerts_SosAlertId",
                        column: x => x.SosAlertId,
                        principalSchema: "device",
                        principalTable: "sos_alerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_device_auth_requests_DeviceCodeHash",
                schema: "device",
                table: "device_auth_requests",
                column: "DeviceCodeHash");

            migrationBuilder.CreateIndex(
                name: "IX_device_auth_requests_UserCode",
                schema: "device",
                table: "device_auth_requests",
                column: "UserCode");

            migrationBuilder.CreateIndex(
                name: "IX_linked_devices_RunnerUserId",
                schema: "device",
                table: "linked_devices",
                column: "RunnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_CreatedOnUtc",
                schema: "device",
                table: "outbox_messages",
                column: "CreatedOnUtc");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_ProcessedOnUtc",
                schema: "device",
                table: "outbox_messages",
                column: "ProcessedOnUtc");

            migrationBuilder.CreateIndex(
                name: "IX_sos_alerts_EventId",
                schema: "device",
                table: "sos_alerts",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_sos_alerts_RunnerUserId",
                schema: "device",
                table: "sos_alerts",
                column: "RunnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_sos_locations_SosAlertId",
                schema: "device",
                table: "sos_locations",
                column: "SosAlertId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_auth_requests",
                schema: "device");

            migrationBuilder.DropTable(
                name: "linked_devices",
                schema: "device");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "device");

            migrationBuilder.DropTable(
                name: "sos_locations",
                schema: "device");

            migrationBuilder.DropTable(
                name: "sos_alerts",
                schema: "device");
        }
    }
}
