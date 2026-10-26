using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RaceDay.Modules.Iam.Infrastructure.Persistence;

#nullable disable

namespace RaceDay.Modules.Iam.Infrastructure.Persistence.Migrations;

[DbContext(typeof(IamDbContext))]
[Migration("20261020103000_AddUserIdNumber")]
public partial class AddUserIdNumber : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "id_number",
            schema: "iam",
            table: "users",
            type: "character varying(30)",
            maxLength: 30,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "id_number",
            schema: "iam",
            table: "users");
    }
}
