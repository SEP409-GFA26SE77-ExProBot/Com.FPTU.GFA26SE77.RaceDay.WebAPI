using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddComplaintTitleAndEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EvidenceLinksJson",
                schema: "event",
                table: "event_complaints",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Title",
                schema: "event",
                table: "event_complaints",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EvidenceLinksJson",
                schema: "event",
                table: "event_complaints");

            migrationBuilder.DropColumn(
                name: "Title",
                schema: "event",
                table: "event_complaints");
        }
    }
}
