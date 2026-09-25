using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Domain.Roles;
using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Modules.Iam.Infrastructure.Persistence;

namespace RaceDay.Modules.Iam.Infrastructure.Persistence;

public sealed class IamDataSeeder(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<IamDataSeeder> logger) : IHostedService
{
    private static readonly Guid AdminRoleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserRoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OrganizerRoleId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IamDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        try
        {
            await StampMigrationIfSchemaAlreadyExistsAsync(db, cancellationToken);
            await db.Database.MigrateAsync(cancellationToken);
            await EnsureOutboxTableAsync(db, cancellationToken);
            await EnsureEmailOtpsTableAsync(db, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "IAM database migrate failed.");
            throw;
        }

        await SeedRolesAsync(db, cancellationToken);

        if (!await db.Users.AnyAsync(cancellationToken))
        {
            await SeedUsersAsync(db, hasher, RequireSeedPassword(), cancellationToken);
        }

        await UpgradePlaceholderPasswordsAsync(db, hasher, cancellationToken);
        logger.LogInformation("IAM seed completed.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task EnsureOutboxTableAsync(IamDbContext db, CancellationToken ct)
    {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS iam.outbox_messages (
                    id uuid PRIMARY KEY,
                    event_type varchar(500) NOT NULL,
                    payload text NOT NULL,
                    occurred_on_utc timestamptz NOT NULL,
                    created_on_utc timestamptz NOT NULL,
                    processed_on_utc timestamptz NULL
                );
                """,
                ct);
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS ix_outbox_messages_processed_on_utc ON iam.outbox_messages (processed_on_utc);
                """,
                ct);
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS ix_outbox_messages_created_on_utc ON iam.outbox_messages (created_on_utc);
                """,
                ct);
    }

    private static async Task EnsureEmailOtpsTableAsync(IamDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS iam.email_otps (
                id uuid PRIMARY KEY,
                user_id uuid NULL,
                email varchar(100) NOT NULL,
                purpose smallint NOT NULL,
                code_hash varchar(128) NOT NULL,
                expires_on_utc timestamptz NOT NULL,
                attempt_count int NOT NULL DEFAULT 0,
                consumed_on_utc timestamptz NULL,
                created_on_utc timestamptz NOT NULL
            );
            """,
            ct);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS ix_email_otps_email_purpose_created
                ON iam.email_otps (email, purpose, created_on_utc);
            """,
            ct);
    }

    private string RequireSeedPassword()
    {
        var password = configuration["Iam:SeedPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Iam:SeedPassword is missing. Set it via .env (Iam__SeedPassword) or configuration.");
        }

        return password;
    }

    private static async Task StampMigrationIfSchemaAlreadyExistsAsync(IamDbContext db, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using (var ensureHistory = connection.CreateCommand())
        {
            ensureHistory.CommandText =
                """
                CREATE TABLE IF NOT EXISTS iam."__EFMigrationsHistory" (
                    "MigrationId" varchar(150) NOT NULL PRIMARY KEY,
                    "ProductVersion" varchar(32) NOT NULL
                );
                """;
            await ensureHistory.ExecuteNonQueryAsync(ct);
        }

        await StampIfTableExistsAsync(connection, "roles", "20260921164029_InitialIam", ct);
        await StampIfTableExistsAsync(connection, "email_otps", "20260921182559_AddOutboxAndEmailOtps", ct);
        await StampIfTableExistsAsync(connection, "outbox_messages", "20260921182559_AddOutboxAndEmailOtps", ct);
    }

    private static async Task StampIfTableExistsAsync(
        System.Data.Common.DbConnection connection,
        string tableName,
        string migrationId,
        CancellationToken ct)
    {
        bool exists;
        await using (var check = connection.CreateCommand())
        {
            check.CommandText =
                """
                SELECT EXISTS (
                    SELECT 1 FROM information_schema.tables
                    WHERE table_schema = 'iam' AND table_name = @table
                );
                """;
            var tableParam = check.CreateParameter();
            tableParam.ParameterName = "table";
            tableParam.Value = tableName;
            check.Parameters.Add(tableParam);
            exists = (bool)(await check.ExecuteScalarAsync(ct))!;
        }

        if (!exists)
        {
            return;
        }

        await using var stamp = connection.CreateCommand();
        stamp.CommandText =
            """
            INSERT INTO iam."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES (@id, '10.0.12')
            ON CONFLICT ("MigrationId") DO NOTHING;
            """;
        var p = stamp.CreateParameter();
        p.ParameterName = "id";
        p.Value = migrationId;
        stamp.Parameters.Add(p);
        await stamp.ExecuteNonQueryAsync(ct);
    }

    private static async Task SeedRolesAsync(IamDbContext db, CancellationToken ct)
    {
        async Task EnsureRole(Guid id, string name, string description)
        {
            if (!await db.Roles.AnyAsync(x => x.Id == id || x.RoleName == name, ct))
            {
                db.Roles.Add(Role.Restore(id, name, description));
            }
        }

        await EnsureRole(AdminRoleId, "Admin", "Quản trị hệ thống RaceDay");
        await EnsureRole(UserRoleId, "User", "Người dùng / vận động viên");
        await EnsureRole(OrganizerRoleId, "Organizer", "Ban tổ chức sự kiện");
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedUsersAsync(
        IamDbContext db,
        IPasswordHasher hasher,
        string seedPassword,
        CancellationToken ct)
    {
        var passwordHash = hasher.Hash(seedPassword);
        var seeds = new[]
        {
            BuildSeed(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "admin@raceday.vn", AdminRoleId, passwordHash,
                "Nguyễn Văn Quản Trị", "0901000001", new DateOnly(1990, 1, 15), Gender.Male, UserStatus.Active, true,
                "Tài khoản quản trị RaceDay", true),
            BuildSeed(
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                "an.nguyen@raceday.vn", UserRoleId, passwordHash,
                "Nguyễn Văn An", "0902000002", new DateOnly(1998, 5, 20), Gender.Male, UserStatus.Active, true,
                "Vận động viên phong trào TP.HCM", true),
            BuildSeed(
                Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                "binh.tran@raceday.vn", UserRoleId, passwordHash,
                "Trần Thị Bình", "0903000003", new DateOnly(1999, 8, 12), Gender.Female, UserStatus.Active, true,
                "Runner bán marathon Hà Nội", true),
            BuildSeed(
                Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                "cuong.le@raceday.vn", OrganizerRoleId, passwordHash,
                "Lê Hoàng Cường", "0904000004", new DateOnly(1995, 3, 8), Gender.Male, UserStatus.Active, true,
                "Ban tổ chức giải chạy Đà Nẵng", true),
            BuildSeed(
                Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                "dung.pham@raceday.vn", UserRoleId, passwordHash,
                "Phạm Minh Dũng", "0905000005", new DateOnly(2000, 11, 25), Gender.Male, UserStatus.Pending, false,
                "Hồ sơ chờ xác minh email", false)
        };

        db.Users.AddRange(seeds);
        await db.SaveChangesAsync(ct);
    }

    private static async Task UpgradePlaceholderPasswordsAsync(
        IamDbContext db,
        IPasswordHasher hasher,
        CancellationToken ct)
    {
        var placeholders = await db.Users
            .Where(x => EF.Functions.Like(x.PasswordHash, "PLACEHOLDER:%"))
            .ToListAsync(ct);

        foreach (var user in placeholders)
        {
            var plain = user.PasswordHash["PLACEHOLDER:".Length..];
            user.SetPasswordHash(hasher.Hash(plain));
            user.ClearDomainEvents();
        }

        if (placeholders.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    private static User BuildSeed(
        Guid id,
        string email,
        Guid roleId,
        string passwordHash,
        string fullName,
        string phone,
        DateOnly dob,
        Gender gender,
        UserStatus status,
        bool emailVerified,
        string profileDescription,
        bool profileVerified)
    {
        var user = User.Create(email, passwordHash, fullName, roleId, phone, dob, gender, status);
        SetEntityId(user, id);
        if (user.Profile is not null)
        {
            SetEntityId(user.Profile, id);
            user.Profile.UpdateContact(profileDescription, email, phone);
            if (profileVerified)
            {
                user.Profile.MarkVerified("system");
            }
        }

        if (emailVerified)
        {
            user.MarkEmailVerified();
        }

        user.ClearDomainEvents();
        return user;
    }

    private static void SetEntityId(object entity, Guid id)
    {
        var prop = entity.GetType().GetProperty("Id")
            ?? entity.GetType().BaseType?.GetProperty("Id");
        prop?.SetValue(entity, id);
    }
}
