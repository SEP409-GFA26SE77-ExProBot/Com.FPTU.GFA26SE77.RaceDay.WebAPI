using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.Payment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPackagePurpose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "RegistrationId",
                schema: "payment",
                table: "payments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                schema: "payment",
                table: "payments",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Registration");

            migrationBuilder.AddColumn<Guid>(
                name: "ReferenceId",
                schema: "payment",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_ReferenceId",
                schema: "payment",
                table: "payments",
                column: "ReferenceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payments_ReferenceId",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "Purpose",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "ReferenceId",
                schema: "payment",
                table: "payments");

            migrationBuilder.AlterColumn<Guid>(
                name: "RegistrationId",
                schema: "payment",
                table: "payments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
