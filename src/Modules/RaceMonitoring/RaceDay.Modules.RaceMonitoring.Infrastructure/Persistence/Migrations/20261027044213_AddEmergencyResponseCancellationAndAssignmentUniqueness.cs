using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmergencyResponseCancellationAndAssignmentUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                schema: "race_monitoring",
                table: "emergency_responses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                schema: "race_monitoring",
                table: "emergency_responses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_emergency_assignments_ResponseId_ResourceId",
                schema: "race_monitoring",
                table: "emergency_assignments",
                columns: new[] { "EmergencyResponseId", "EmergencyResourceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_emergency_assignments_ResponseId_ResourceId",
                schema: "race_monitoring",
                table: "emergency_assignments");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                schema: "race_monitoring",
                table: "emergency_responses");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                schema: "race_monitoring",
                table: "emergency_responses");
        }
    }
}
