using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RaceDay.Modules.Iam.Infrastructure.Persistence;

#nullable disable

namespace RaceDay.Modules.Iam.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(IamDbContext))]
[Migration("20261013180000_AddProfileAvatarUrl")]
public partial class AddProfileAvatarUrl : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "avatar_url",
            schema: "iam",
            table: "profiles",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "avatar_url",
            schema: "iam",
            table: "profiles");
    }
}
