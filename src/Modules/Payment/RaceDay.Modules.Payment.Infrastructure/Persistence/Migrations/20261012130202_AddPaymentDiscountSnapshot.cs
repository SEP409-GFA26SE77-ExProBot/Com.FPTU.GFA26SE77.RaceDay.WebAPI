using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.Payment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentDiscountSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                schema: "payment",
                table: "payments",
                type: "numeric(15,2)",
                precision: 15,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ListAmount",
                schema: "payment",
                table: "payments",
                type: "numeric(15,2)",
                precision: 15,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromoCode",
                schema: "payment",
                table: "payments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "ListAmount",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "PromoCode",
                schema: "payment",
                table: "payments");
        }
    }
}
