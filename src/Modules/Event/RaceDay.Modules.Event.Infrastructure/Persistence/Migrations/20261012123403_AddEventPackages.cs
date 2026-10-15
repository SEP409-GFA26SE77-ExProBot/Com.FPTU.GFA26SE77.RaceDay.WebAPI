using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.Event.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEventPackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PackageExpiresAtUtc",
                schema: "event",
                table: "race_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PackagePurchaseId",
                schema: "event",
                table: "race_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "package_purchases",
                schema: "event",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrgName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OrgIntro = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OrgAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    InvoiceInfo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DurationMonths = table.Column<int>(type: "integer", nullable: false),
                    ParticipantCap = table.Column<int>(type: "integer", nullable: false),
                    PromoCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    BasePrice = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    CapacityFee = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    PackageExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_purchases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_package_purchases_OrganizerUserId",
                schema: "event",
                table: "package_purchases",
                column: "OrganizerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "package_purchases",
                schema: "event");

            migrationBuilder.DropColumn(
                name: "PackageExpiresAtUtc",
                schema: "event",
                table: "race_events");

            migrationBuilder.DropColumn(
                name: "PackagePurchaseId",
                schema: "event",
                table: "race_events");
        }
    }
}
