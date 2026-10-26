using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCoordinatorEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aid_stations",
                schema: "race_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckpointId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aid_stations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "broom_wagons",
                schema: "race_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    VehicleType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_broom_wagons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "contingency_plans",
                schema: "race_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    TriggerCondition = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contingency_plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "emergency_resources",
                schema: "race_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResourceCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_emergency_resources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "emergency_responses",
                schema: "race_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SOSAlertId = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Priority = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_emergency_responses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_emergency_responses_sos_alerts_SOSAlertId",
                        column: x => x.SOSAlertId,
                        principalSchema: "race_monitoring",
                        principalTable: "sos_alerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "route_deviations",
                schema: "race_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DetectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DistanceFromRoute = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_route_deviations", x => x.Id);
                    table.CheckConstraint("CK_route_deviations_DistanceFromRoute", "\"DistanceFromRoute\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "runner_status_histories",
                schema: "race_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    NewStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runner_status_histories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "supply_items",
                schema: "race_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supply_items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "broom_wagon_checkpoint_logs",
                schema: "race_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroomWagonId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckpointId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ScanMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_broom_wagon_checkpoint_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_broom_wagon_checkpoint_logs_broom_wagons_BroomWagonId",
                        column: x => x.BroomWagonId,
                        principalSchema: "race_monitoring",
                        principalTable: "broom_wagons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contingency_incidents",
                schema: "race_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContingencyPlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    IncidentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    DetectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contingency_incidents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contingency_incidents_contingency_plans_ContingencyPlanId",
                        column: x => x.ContingencyPlanId,
                        principalSchema: "race_monitoring",
                        principalTable: "contingency_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "emergency_assignments",
                schema: "race_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmergencyResponseId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmergencyResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArrivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_emergency_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_emg_assignments_resources_ResourceId",
                        column: x => x.EmergencyResourceId,
                        principalSchema: "race_monitoring",
                        principalTable: "emergency_resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_emg_assignments_responses_ResponseId",
                        column: x => x.EmergencyResponseId,
                        principalSchema: "race_monitoring",
                        principalTable: "emergency_responses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "aid_station_inventories",
                schema: "race_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AidStationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplyItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuantityAvailable = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    LastUpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aid_station_inventories", x => x.Id);
                    table.CheckConstraint("CK_aid_station_inventories_QuantityAvailable", "\"QuantityAvailable\" >= 0");
                    table.ForeignKey(
                        name: "FK_aid_station_inventories_aid_stations_AidStationId",
                        column: x => x.AidStationId,
                        principalSchema: "race_monitoring",
                        principalTable: "aid_stations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aid_station_inventories_supply_items_SupplyItemId",
                        column: x => x.SupplyItemId,
                        principalSchema: "race_monitoring",
                        principalTable: "supply_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "emergency_decisions",
                schema: "race_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContingencyIncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DecisionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DecisionDetails = table.Column<string>(type: "text", nullable: false),
                    DecidedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_emergency_decisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_emg_decisions_incidents_IncidentId",
                        column: x => x.ContingencyIncidentId,
                        principalSchema: "race_monitoring",
                        principalTable: "contingency_incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "aid_station_supply_transactions",
                schema: "race_monitoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AidStationInventoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aid_station_supply_transactions", x => x.Id);
                    table.CheckConstraint("CK_aid_station_supply_transactions_Quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_aid_supply_tx_inventories_InventoryId",
                        column: x => x.AidStationInventoryId,
                        principalSchema: "race_monitoring",
                        principalTable: "aid_station_inventories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_aid_station_inventories_AidStationId_SupplyItemId",
                schema: "race_monitoring",
                table: "aid_station_inventories",
                columns: new[] { "AidStationId", "SupplyItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_aid_station_inventories_SupplyItemId",
                schema: "race_monitoring",
                table: "aid_station_inventories",
                column: "SupplyItemId");

            migrationBuilder.CreateIndex(
                name: "IX_aid_supply_tx_InventoryId_OccurredAtUtc",
                schema: "race_monitoring",
                table: "aid_station_supply_transactions",
                columns: new[] { "AidStationInventoryId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_aid_stations_CheckpointId",
                schema: "race_monitoring",
                table: "aid_stations",
                column: "CheckpointId");

            migrationBuilder.CreateIndex(
                name: "IX_aid_stations_EventId",
                schema: "race_monitoring",
                table: "aid_stations",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_broom_wagon_checkpoint_logs_BroomWagonId_RecordedAtUtc",
                schema: "race_monitoring",
                table: "broom_wagon_checkpoint_logs",
                columns: new[] { "BroomWagonId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_broom_wagon_checkpoint_logs_CheckpointId_RecordedAtUtc",
                schema: "race_monitoring",
                table: "broom_wagon_checkpoint_logs",
                columns: new[] { "CheckpointId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_broom_wagons_EventId_VehicleCode",
                schema: "race_monitoring",
                table: "broom_wagons",
                columns: new[] { "EventId", "VehicleCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contingency_incidents_ContingencyPlanId",
                schema: "race_monitoring",
                table: "contingency_incidents",
                column: "ContingencyPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_contingency_incidents_EventId_DetectedAtUtc",
                schema: "race_monitoring",
                table: "contingency_incidents",
                columns: new[] { "EventId", "DetectedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_contingency_plans_EventId_Status",
                schema: "race_monitoring",
                table: "contingency_plans",
                columns: new[] { "EventId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_emergency_assignments_EmergencyResourceId_AssignedAtUtc",
                schema: "race_monitoring",
                table: "emergency_assignments",
                columns: new[] { "EmergencyResourceId", "AssignedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_emergency_assignments_EmergencyResponseId_AssignedAtUtc",
                schema: "race_monitoring",
                table: "emergency_assignments",
                columns: new[] { "EmergencyResponseId", "AssignedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_emergency_decisions_ContingencyIncidentId_DecidedAtUtc",
                schema: "race_monitoring",
                table: "emergency_decisions",
                columns: new[] { "ContingencyIncidentId", "DecidedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_emergency_resources_EventId_ResourceCode",
                schema: "race_monitoring",
                table: "emergency_resources",
                columns: new[] { "EventId", "ResourceCode" },
                unique: true,
                filter: "\"EventId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_emergency_resources_ResourceCode_NoEvent",
                schema: "race_monitoring",
                table: "emergency_resources",
                column: "ResourceCode",
                unique: true,
                filter: "\"EventId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_emergency_resources_Status",
                schema: "race_monitoring",
                table: "emergency_resources",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_emergency_responses_SOSAlertId",
                schema: "race_monitoring",
                table: "emergency_responses",
                column: "SOSAlertId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_emergency_responses_Status_Priority",
                schema: "race_monitoring",
                table: "emergency_responses",
                columns: new[] { "Status", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_route_deviations_RegistrationId_DetectedAtUtc",
                schema: "race_monitoring",
                table: "route_deviations",
                columns: new[] { "RegistrationId", "DetectedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_route_deviations_Status_DetectedAtUtc",
                schema: "race_monitoring",
                table: "route_deviations",
                columns: new[] { "Status", "DetectedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_runner_status_histories_RegistrationId_ChangedAtUtc",
                schema: "race_monitoring",
                table: "runner_status_histories",
                columns: new[] { "RegistrationId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_supply_items_Name_Unit",
                schema: "race_monitoring",
                table: "supply_items",
                columns: new[] { "Name", "Unit" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aid_station_supply_transactions",
                schema: "race_monitoring");

            migrationBuilder.DropTable(
                name: "broom_wagon_checkpoint_logs",
                schema: "race_monitoring");

            migrationBuilder.DropTable(
                name: "emergency_assignments",
                schema: "race_monitoring");

            migrationBuilder.DropTable(
                name: "emergency_decisions",
                schema: "race_monitoring");

            migrationBuilder.DropTable(
                name: "route_deviations",
                schema: "race_monitoring");

            migrationBuilder.DropTable(
                name: "runner_status_histories",
                schema: "race_monitoring");

            migrationBuilder.DropTable(
                name: "aid_station_inventories",
                schema: "race_monitoring");

            migrationBuilder.DropTable(
                name: "broom_wagons",
                schema: "race_monitoring");

            migrationBuilder.DropTable(
                name: "emergency_resources",
                schema: "race_monitoring");

            migrationBuilder.DropTable(
                name: "emergency_responses",
                schema: "race_monitoring");

            migrationBuilder.DropTable(
                name: "contingency_incidents",
                schema: "race_monitoring");

            migrationBuilder.DropTable(
                name: "aid_stations",
                schema: "race_monitoring");

            migrationBuilder.DropTable(
                name: "supply_items",
                schema: "race_monitoring");

            migrationBuilder.DropTable(
                name: "contingency_plans",
                schema: "race_monitoring");
        }
    }
}
