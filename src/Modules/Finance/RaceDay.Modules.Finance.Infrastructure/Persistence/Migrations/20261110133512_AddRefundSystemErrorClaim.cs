using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRefundSystemErrorClaim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSystemErrorClaim",
                schema: "finance",
                table: "refund_requests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_refund_requests_IsSystemErrorClaim_Status",
                schema: "finance",
                table: "refund_requests",
                columns: new[] { "IsSystemErrorClaim", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_refund_requests_IsSystemErrorClaim_Status",
                schema: "finance",
                table: "refund_requests");

            migrationBuilder.DropColumn(
                name: "IsSystemErrorClaim",
                schema: "finance",
                table: "refund_requests");
        }
    }
}
