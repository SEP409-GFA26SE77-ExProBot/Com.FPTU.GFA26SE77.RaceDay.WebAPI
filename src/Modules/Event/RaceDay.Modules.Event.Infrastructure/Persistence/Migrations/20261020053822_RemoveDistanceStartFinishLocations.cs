using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDistanceStartFinishLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "finish_lat",
                schema: "event",
                table: "event_distances");

            migrationBuilder.DropColumn(
                name: "finish_lng",
                schema: "event",
                table: "event_distances");

            migrationBuilder.DropColumn(
                name: "finish_name",
                schema: "event",
                table: "event_distances");

            migrationBuilder.DropColumn(
                name: "start_lat",
                schema: "event",
                table: "event_distances");

            migrationBuilder.DropColumn(
                name: "start_lng",
                schema: "event",
                table: "event_distances");

            migrationBuilder.DropColumn(
                name: "start_name",
                schema: "event",
                table: "event_distances");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "finish_lat",
                schema: "event",
                table: "event_distances",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "finish_lng",
                schema: "event",
                table: "event_distances",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "finish_name",
                schema: "event",
                table: "event_distances",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "start_lat",
                schema: "event",
                table: "event_distances",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "start_lng",
                schema: "event",
                table: "event_distances",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "start_name",
                schema: "event",
                table: "event_distances",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }
    }
}
