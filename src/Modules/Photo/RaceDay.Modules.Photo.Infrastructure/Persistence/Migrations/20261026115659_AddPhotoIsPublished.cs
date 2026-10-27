using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.Photo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotoIsPublished : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPublished",
                schema: "photo",
                table: "photos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAtUtc",
                schema: "photo",
                table: "photos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_photos_EventId_IsPublished_Status",
                schema: "photo",
                table: "photos",
                columns: new[] { "EventId", "IsPublished", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_photos_EventId_IsPublished_Status",
                schema: "photo",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "IsPublished",
                schema: "photo",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "PublishedAtUtc",
                schema: "photo",
                table: "photos");
        }
    }
}
