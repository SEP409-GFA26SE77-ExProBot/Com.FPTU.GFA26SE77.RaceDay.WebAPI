using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDistanceElevationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ElevationGainM",
                schema: "event",
                table: "event_distances");

            migrationBuilder.DropColumn(
                name: "MaxElevationM",
                schema: "event",
                table: "event_distances");

            migrationBuilder.DropColumn(
                name: "MinElevationM",
                schema: "event",
                table: "event_distances");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ElevationGainM",
                schema: "event",
                table: "event_distances",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "MaxElevationM",
                schema: "event",
                table: "event_distances",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "MinElevationM",
                schema: "event",
                table: "event_distances",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);
        }
    }
}
