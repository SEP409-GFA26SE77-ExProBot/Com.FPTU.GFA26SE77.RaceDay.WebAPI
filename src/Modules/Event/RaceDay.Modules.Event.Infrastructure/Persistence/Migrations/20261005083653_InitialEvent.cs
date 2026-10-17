using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "event");

            migrationBuilder.CreateTable(
                name: "event_registrations",
                schema: "event",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    DistanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FullName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    Gender = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    IdNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    HeightCm = table.Column<int>(type: "integer", nullable: true),
                    WeightKg = table.Column<int>(type: "integer", nullable: true),
                    BloodType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    EmergencyName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EmergencyPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ShirtType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ShirtSize = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    ShirtColor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    MedicalCodesJson = table.Column<string>(type: "jsonb", nullable: false),
                    HealthCertFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    HealthCertUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LiabilityAccepted = table.Column<bool>(type: "boolean", nullable: false),
                    LiabilityAcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    BibNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_registrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "race_events",
                schema: "event",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Tagline = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    coord_lat = table.Column<double>(type: "double precision", nullable: false),
                    coord_lng = table.Column<double>(type: "double precision", nullable: false),
                    EventDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    org_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    org_legal_entity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    org_email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    org_phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    org_website = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    org_verified = table.Column<bool>(type: "boolean", nullable: false),
                    org_account_holder = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    org_account_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    org_bank_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    org_bank_branch = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    org_swift = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    org_transfer_note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    TotalRunnersCap = table.Column<int>(type: "integer", nullable: false),
                    weather_temp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    weather_condition = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    weather_humidity = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    permit_file_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    permit_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    permit_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    permit_authority = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    permit_issue_date = table.Column<DateOnly>(type: "date", nullable: true),
                    permit_valid_until = table.Column<DateOnly>(type: "date", nullable: true),
                    TicketPricingJson = table.Column<string>(type: "jsonb", nullable: false),
                    MerchandiseJson = table.Column<string>(type: "jsonb", nullable: false),
                    RulesJson = table.Column<string>(type: "jsonb", nullable: false),
                    RewardsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_race_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "event_distances",
                schema: "event",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DistanceKm = table.Column<double>(type: "double precision", nullable: false),
                    ElevationGainM = table.Column<double>(type: "double precision", nullable: false),
                    MaxElevationM = table.Column<double>(type: "double precision", nullable: false),
                    MinElevationM = table.Column<double>(type: "double precision", nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    RegisteredCount = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    start_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    start_lat = table.Column<double>(type: "double precision", nullable: false),
                    start_lng = table.Column<double>(type: "double precision", nullable: false),
                    finish_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    finish_lat = table.Column<double>(type: "double precision", nullable: false),
                    finish_lng = table.Column<double>(type: "double precision", nullable: false),
                    RouteGeometryJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_distances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_distances_race_events_EventId",
                        column: x => x.EventId,
                        principalSchema: "event",
                        principalTable: "race_events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_documents",
                schema: "event",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    FileUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Required = table.Column<bool>(type: "boolean", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_documents_race_events_EventId",
                        column: x => x.EventId,
                        principalSchema: "event",
                        principalTable: "race_events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_checkpoints",
                schema: "event",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DistanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Km = table.Column<double>(type: "double precision", nullable: false),
                    Lat = table.Column<double>(type: "double precision", nullable: false),
                    Lng = table.Column<double>(type: "double precision", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Cutoff = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Elevation = table.Column<int>(type: "integer", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_checkpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_checkpoints_event_distances_DistanceId",
                        column: x => x.DistanceId,
                        principalSchema: "event",
                        principalTable: "event_distances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_event_checkpoints_DistanceId",
                schema: "event",
                table: "event_checkpoints",
                column: "DistanceId");

            migrationBuilder.CreateIndex(
                name: "IX_event_distances_EventId",
                schema: "event",
                table: "event_distances",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_event_documents_EventId",
                schema: "event",
                table: "event_documents",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_event_registrations_EventId",
                schema: "event",
                table: "event_registrations",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_event_registrations_EventId_DistanceId_UserId",
                schema: "event",
                table: "event_registrations",
                columns: new[] { "EventId", "DistanceId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_race_events_EventDate",
                schema: "event",
                table: "race_events",
                column: "EventDate");

            migrationBuilder.CreateIndex(
                name: "IX_race_events_OrganizerUserId",
                schema: "event",
                table: "race_events",
                column: "OrganizerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_race_events_Status",
                schema: "event",
                table: "race_events",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_checkpoints",
                schema: "event");

            migrationBuilder.DropTable(
                name: "event_documents",
                schema: "event");

            migrationBuilder.DropTable(
                name: "event_registrations",
                schema: "event");

            migrationBuilder.DropTable(
                name: "event_distances",
                schema: "event");

            migrationBuilder.DropTable(
                name: "race_events",
                schema: "event");
        }
    }
}
