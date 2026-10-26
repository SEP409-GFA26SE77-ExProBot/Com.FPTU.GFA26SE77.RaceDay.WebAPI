using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.Photo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPhoto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "photo");

            migrationBuilder.CreateTable(
                name: "photo_disputes",
                schema: "photo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PhotoId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReporterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ReviewerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_photo_disputes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "photos",
                schema: "photo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ContentSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    Zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DiscardReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OcrJobId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_photos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "photo_tags",
                schema: "photo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PhotoId = table.Column<Guid>(type: "uuid", nullable: false),
                    BibNormalized = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    BboxJson = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_photo_tags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_photo_tags_photos_PhotoId",
                        column: x => x.PhotoId,
                        principalSchema: "photo",
                        principalTable: "photos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_photo_disputes_EventId_Status",
                schema: "photo",
                table: "photo_disputes",
                columns: new[] { "EventId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_photo_disputes_ReporterUserId",
                schema: "photo",
                table: "photo_disputes",
                column: "ReporterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_photo_tags_BibNormalized_PhotoId",
                schema: "photo",
                table: "photo_tags",
                columns: new[] { "BibNormalized", "PhotoId" });

            migrationBuilder.CreateIndex(
                name: "IX_photo_tags_PhotoId",
                schema: "photo",
                table: "photo_tags",
                column: "PhotoId");

            migrationBuilder.CreateIndex(
                name: "IX_photos_EventId_ContentSha256",
                schema: "photo",
                table: "photos",
                columns: new[] { "EventId", "ContentSha256" });

            migrationBuilder.CreateIndex(
                name: "IX_photos_EventId_Status",
                schema: "photo",
                table: "photos",
                columns: new[] { "EventId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "photo_disputes",
                schema: "photo");

            migrationBuilder.DropTable(
                name: "photo_tags",
                schema: "photo");

            migrationBuilder.DropTable(
                name: "photos",
                schema: "photo");
        }
    }
}
