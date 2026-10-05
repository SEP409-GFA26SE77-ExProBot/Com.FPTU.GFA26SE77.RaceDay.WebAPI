using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceDay.Modules.Iam.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(IamDbContext))]
[Migration("20261005120000_AddUsername")]
public partial class AddUsername : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "username",
            schema: "iam",
            table: "users",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE iam.users
            SET username = lower(split_part(email, '@', 1))
            WHERE username IS NULL OR btrim(username) = '';
            """);

        migrationBuilder.Sql(
            """
            UPDATE iam.users u
            SET username = lower(split_part(u.email, '@', 1)) || '_' || left(replace(u.user_id::text, '-', ''), 8)
            WHERE EXISTS (
                SELECT 1 FROM iam.users x
                WHERE x.username = u.username AND x.user_id <> u.user_id
            );
            """);

        migrationBuilder.AlterColumn<string>(
            name: "username",
            schema: "iam",
            table: "users",
            type: "character varying(50)",
            maxLength: 50,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(50)",
            oldMaxLength: 50,
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_users_username",
            schema: "iam",
            table: "users",
            column: "username",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_users_username",
            schema: "iam",
            table: "users");

        migrationBuilder.DropColumn(
            name: "username",
            schema: "iam",
            table: "users");
    }
}
