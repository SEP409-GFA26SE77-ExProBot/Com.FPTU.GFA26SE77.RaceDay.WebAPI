using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
    private static readonly Guid CoordinatorRoleId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid PhotographerRoleId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid VolunteerRoleId = Guid.Parse("66666666-6666-6666-6666-666666666666");

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
            await EnsureUsernameColumnAsync(db, cancellationToken);
            await BackfillMissingUsernamesFromFullNameAsync(db, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "IAM database migrate failed.");
            throw;
        }

        await SeedRolesAsync(db, cancellationToken);

        var seedPassword = RequireSeedPassword();
        if (!await db.Users.AnyAsync(cancellationToken))
        {
            await SeedUsersAsync(db, hasher, seedPassword, cancellationToken);
        }

        // Idempotent — DB deploy đã có user vẫn bổ sung tài khoản staff ops.
        await EnsureStaffUsersAsync(db, hasher, seedPassword, cancellationToken);

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

    private static async Task EnsureUsernameColumnAsync(IamDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE iam.users ADD COLUMN IF NOT EXISTS username varchar(50);
            """,
            ct);

        // Leave empty rows for BackfillMissingUsernamesFromFullNameAsync (fullname rút gọn).
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS ix_users_username ON iam.users (username)
            WHERE username IS NOT NULL AND btrim(username) <> '';
            """,
            ct);
    }

    private static async Task BackfillMissingUsernamesFromFullNameAsync(IamDbContext db, CancellationToken ct)
    {
        var users = await db.Users
            .Where(x => string.IsNullOrWhiteSpace(x.Username))
            .ToListAsync(ct);

        if (users.Count == 0)
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE iam.users ALTER COLUMN username SET NOT NULL;
                """,
                ct);
            return;
        }

        var taken = await db.Users
            .Where(x => !string.IsNullOrWhiteSpace(x.Username))
            .Select(x => x.Username)
            .ToListAsync(ct);
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);

        foreach (var user in users)
        {
            var candidate = SuggestUsernameFromFullName(user.FullName);
            var unique = candidate;
            var n = 1;
            while (used.Contains(unique))
            {
                var suffix = $"_{n}";
                unique = candidate[..Math.Min(candidate.Length, User.UsernameMaxLength - suffix.Length)] + suffix;
                n++;
            }

            user.ChangeUsername(unique);
            user.ClearDomainEvents();
            used.Add(unique);
        }

        await db.SaveChangesAsync(ct);
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE iam.users ALTER COLUMN username SET NOT NULL;
            """,
            ct);
    }

    /// <summary>Vietnamese full name → given.family without diacritics (e.g. Nguyễn Văn An → an.nguyen).</summary>
    internal static string SuggestUsernameFromFullName(string fullName)
    {
        var ascii = RemoveDiacritics(fullName ?? string.Empty).ToLowerInvariant();
        var parts = Regex.Split(ascii, @"[^a-z0-9]+")
            .Where(static p => p.Length > 0)
            .ToArray();

        string baseName;
        if (parts.Length == 0)
        {
            baseName = "user";
        }
        else if (parts.Length == 1)
        {
            baseName = parts[0];
        }
        else
        {
            baseName = $"{parts[^1]}.{parts[0]}";
        }

        baseName = Regex.Replace(baseName, @"[^a-z0-9._-]", string.Empty);
        if (baseName.Length > User.UsernameMaxLength)
        {
            baseName = baseName[..User.UsernameMaxLength];
        }

        if (baseName.Length < User.UsernameMinLength)
        {
            baseName = (baseName + "user")[..Math.Min(User.UsernameMaxLength, Math.Max(User.UsernameMinLength, baseName.Length + 4))];
        }

        return baseName;
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
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
        await EnsureRole(CoordinatorRoleId, "Coordinator", "Điều phối viên giải");
        await EnsureRole(PhotographerRoleId, "Photographer", "Nhiếp ảnh giải");
        await EnsureRole(VolunteerRoleId, "Volunteer", "Tình nguyện viên giải");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// 4 account / role (Coordinator, Photographer, Volunteer) — skip nếu email đã tồn tại.
    /// </summary>
    private static async Task EnsureStaffUsersAsync(
        IamDbContext db,
        IPasswordHasher hasher,
        string seedPassword,
        CancellationToken ct)
    {
        var passwordHash = hasher.Hash(seedPassword);
        var seeds = BuildStaffSeeds(passwordHash);
        var added = 0;
        foreach (var seed in seeds)
        {
            if (await db.Users.AnyAsync(x => x.Email == seed.Email || x.Username == seed.Username, ct))
            {
                continue;
            }

            db.Users.Add(seed);
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    private static User[] BuildStaffSeeds(string passwordHash) =>
    [
        BuildSeed(Guid.Parse("a1444444-0001-4000-8000-000000000001"),
            "coord1@raceday.vn", "coord1", CoordinatorRoleId, passwordHash,
            "Nguyễn Điều Phối Một", "0911000001", new DateOnly(1994, 2, 10), Gender.Male, UserStatus.Active, true,
            "Coordinator seed #1", true),
        BuildSeed(Guid.Parse("a1444444-0002-4000-8000-000000000002"),
            "coord2@raceday.vn", "coord2", CoordinatorRoleId, passwordHash,
            "Trần Điều Phối Hai", "0911000002", new DateOnly(1993, 6, 18), Gender.Female, UserStatus.Active, true,
            "Coordinator seed #2", true),
        BuildSeed(Guid.Parse("a1444444-0003-4000-8000-000000000003"),
            "coord3@raceday.vn", "coord3", CoordinatorRoleId, passwordHash,
            "Lê Điều Phối Ba", "0911000003", new DateOnly(1996, 9, 5), Gender.Male, UserStatus.Active, true,
            "Coordinator seed #3", true),
        BuildSeed(Guid.Parse("a1444444-0004-4000-8000-000000000004"),
            "coord4@raceday.vn", "coord4", CoordinatorRoleId, passwordHash,
            "Phạm Điều Phối Tư", "0911000004", new DateOnly(1992, 12, 22), Gender.Female, UserStatus.Active, true,
            "Coordinator seed #4", true),

        BuildSeed(Guid.Parse("a1555555-0001-4000-8000-000000000001"),
            "photo1@raceday.vn", "photo1", PhotographerRoleId, passwordHash,
            "Hoàng Ảnh Một", "0912000001", new DateOnly(1995, 4, 8), Gender.Male, UserStatus.Active, true,
            "Photographer seed #1", true),
        BuildSeed(Guid.Parse("a1555555-0002-4000-8000-000000000002"),
            "photo2@raceday.vn", "photo2", PhotographerRoleId, passwordHash,
            "Vũ Ảnh Hai", "0912000002", new DateOnly(1997, 7, 14), Gender.Female, UserStatus.Active, true,
            "Photographer seed #2", true),
        BuildSeed(Guid.Parse("a1555555-0003-4000-8000-000000000003"),
            "photo3@raceday.vn", "photo3", PhotographerRoleId, passwordHash,
            "Đặng Ảnh Ba", "0912000003", new DateOnly(1994, 1, 30), Gender.Male, UserStatus.Active, true,
            "Photographer seed #3", true),
        BuildSeed(Guid.Parse("a1555555-0004-4000-8000-000000000004"),
            "photo4@raceday.vn", "photo4", PhotographerRoleId, passwordHash,
            "Bùi Ảnh Tư", "0912000004", new DateOnly(1998, 10, 3), Gender.Female, UserStatus.Active, true,
            "Photographer seed #4", true),

        BuildSeed(Guid.Parse("a1666666-0001-4000-8000-000000000001"),
            "vol1@raceday.vn", "vol1", VolunteerRoleId, passwordHash,
            "Ngô Tình Nguyện Một", "0913000001", new DateOnly(2001, 3, 12), Gender.Male, UserStatus.Active, true,
            "Volunteer seed #1", true),
        BuildSeed(Guid.Parse("a1666666-0002-4000-8000-000000000002"),
            "vol2@raceday.vn", "vol2", VolunteerRoleId, passwordHash,
            "Đỗ Tình Nguyện Hai", "0913000002", new DateOnly(2000, 5, 21), Gender.Female, UserStatus.Active, true,
            "Volunteer seed #2", true),
        BuildSeed(Guid.Parse("a1666666-0003-4000-8000-000000000003"),
            "vol3@raceday.vn", "vol3", VolunteerRoleId, passwordHash,
            "Mai Tình Nguyện Ba", "0913000003", new DateOnly(1999, 8, 9), Gender.Male, UserStatus.Active, true,
            "Volunteer seed #3", true),
        BuildSeed(Guid.Parse("a1666666-0004-4000-8000-000000000004"),
            "vol4@raceday.vn", "vol4", VolunteerRoleId, passwordHash,
            "Lý Tình Nguyện Tư", "0913000004", new DateOnly(2002, 11, 17), Gender.Female, UserStatus.Active, true,
            "Volunteer seed #4", true),
    ];

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
                "admin@raceday.vn", "admin", AdminRoleId, passwordHash,
                "Nguyễn Văn Quản Trị", "0901000001", new DateOnly(1990, 1, 15), Gender.Male, UserStatus.Active, true,
                "Tài khoản quản trị RaceDay", true),
            BuildSeed(
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                "an.nguyen@raceday.vn", "an.nguyen", UserRoleId, passwordHash,
                "Nguyễn Văn An", "0902000002", new DateOnly(1998, 5, 20), Gender.Male, UserStatus.Active, true,
                "Vận động viên phong trào TP.HCM", true),
            BuildSeed(
                Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                "binh.tran@raceday.vn", "binh.tran", UserRoleId, passwordHash,
                "Trần Thị Bình", "0903000003", new DateOnly(1999, 8, 12), Gender.Female, UserStatus.Active, true,
                "Runner bán marathon Hà Nội", true),
            BuildSeed(
                Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                "cuong.le@raceday.vn", "cuong.le", OrganizerRoleId, passwordHash,
                "Lê Hoàng Cường", "0904000004", new DateOnly(1995, 3, 8), Gender.Male, UserStatus.Active, true,
                "Ban tổ chức giải chạy Đà Nẵng", true),
            BuildSeed(
                Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                "dung.pham@raceday.vn", "dung.pham", UserRoleId, passwordHash,
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
        string username,
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
        var user = User.Create(email, username, passwordHash, fullName, roleId, phone, dob, gender, status);
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
