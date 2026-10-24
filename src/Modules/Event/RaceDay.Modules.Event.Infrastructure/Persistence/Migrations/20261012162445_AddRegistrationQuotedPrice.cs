using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationQuotedPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "QuotedAmount",
                schema: "event",
                table: "event_registrations",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuotedPhaseKey",
                schema: "event",
                table: "event_registrations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuotedPhaseName",
                schema: "event",
                table: "event_registrations",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QuotedAmount",
                schema: "event",
                table: "event_registrations");

            migrationBuilder.DropColumn(
                name: "QuotedPhaseKey",
                schema: "event",
                table: "event_registrations");

            migrationBuilder.DropColumn(
                name: "QuotedPhaseName",
                schema: "event",
                table: "event_registrations");
        }
    }
}
