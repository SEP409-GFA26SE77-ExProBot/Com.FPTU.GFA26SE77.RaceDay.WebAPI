using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RaceDay.Modules.Iam.Infrastructure.Persistence;

#nullable disable

namespace RaceDay.Modules.Iam.Infrastructure.Persistence.Migrations;

[DbContext(typeof(IamDbContext))]
[Migration("20261013190000_AddUserMustChangePassword")]
public partial class AddUserMustChangePassword : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "must_change_password",
            schema: "iam",
            table: "users",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "must_change_password",
            schema: "iam",
            table: "users");
    }
}
