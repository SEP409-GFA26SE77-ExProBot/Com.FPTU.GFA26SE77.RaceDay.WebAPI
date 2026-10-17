using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Application.Packages;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Packages;
using RaceDay.Modules.Event.Infrastructure.Packages;
using RaceDay.Modules.Event.Infrastructure.Persistence;
using RaceDay.Modules.Event.Infrastructure.Repositories;

namespace RaceDay.Modules.Event.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddEventInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<EventDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "event")));

        services.AddScoped<IRaceEventRepository, RaceEventRepository>();
        services.AddScoped<IEventRegistrationRepository, EventRegistrationRepository>();
        services.AddScoped<IRegistrationCheckpointPassageRepository, RegistrationCheckpointPassageRepository>();
        services.AddScoped<IEventPackagePurchaseRepository, EventPackagePurchaseRepository>();
        services.AddScoped<IPromoCodeRepository, PromoCodeRepository>();
        services.AddScoped<IPackageDurationOptionRepository, PackageDurationOptionRepository>();
        services.AddScoped<IPackageCapacityOptionRepository, PackageCapacityOptionRepository>();
        services.AddScoped<IEventStaffMemberRepository, EventStaffMemberRepository>();
        services.AddScoped<PackagePricingService>();
        services.AddScoped<IEventUnitOfWork, EventUnitOfWork>();
        services.AddHostedService<PackageExpiryLockHostedService>();
        services.AddHostedService<RegistrationWindowHostedService>();
        services.AddScoped<RaceDay.Modules.Event.Contracts.Registrations.IRegistrationCheckoutInfoQuery,
            RaceDay.Modules.Event.Infrastructure.Registrations.RegistrationCheckoutInfoQuery>();
        services.AddScoped<RaceDay.Modules.Event.Contracts.Packages.IPackageCheckoutInfoQuery,
            PackageCheckoutInfoQuery>();
        services.AddScoped<RaceDay.Modules.Event.Contracts.Events.IEventDistanceAccessQuery,
            RaceDay.Modules.Event.Infrastructure.Events.EventDistanceAccessQuery>();
        services.AddScoped<RaceDay.Modules.Event.Contracts.Finance.IEventFinanceQuery,
            RaceDay.Modules.Event.Infrastructure.Finance.EventFinanceQuery>();
        services.AddScoped<RaceDay.Modules.Event.Contracts.Finance.IRegistrationCapacityPort,
            RaceDay.Modules.Event.Infrastructure.Finance.RegistrationCapacityPort>();
        services.AddHostedService<EventDataSeeder>();
        return services;
    }
}

public sealed class EventDataSeeder(
    IServiceScopeFactory scopeFactory,
    ILogger<EventDataSeeder> logger) : IHostedService
{
    // Stable demo ids — FE / smoke can deep-link. aaa1 = flagship Viet run (Quy Nhơn).
    private static readonly Guid DemoOrganizerId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"); // cuong.le@raceday.vn
    private static readonly Guid DemoEventQuyNhonId = Guid.Parse("11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1");
    private static readonly Guid DemoEventSapaId = Guid.Parse("11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa2");
    private static readonly Guid DemoDist5k = Guid.Parse("22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb1");
    private static readonly Guid DemoDist10k = Guid.Parse("22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb2");
    private static readonly Guid DemoDist21k = Guid.Parse("22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb4");
    private static readonly Guid DemoDist42k = Guid.Parse("22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb5");
    private static readonly Guid DemoDistSapa21k = Guid.Parse("22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb3");

    private const string BannerQuyNhon =
        "https://pub-36bb0ea536ef493b9c291b1bf1ec2e20.r2.dev/event-banners/20261010153523-b062278b2115439abd42da13aa689368.JPG";
    private const string DocQuyNhon =
        "https://pub-36bb0ea536ef493b9c291b1bf1ec2e20.r2.dev/event-docs/20261006082053-8e6f2c0a95404763801228bfaf40ec93.docx";
    private const string HealthCert =
        "https://pub-36bb0ea536ef493b9c291b1bf1ec2e20.r2.dev/health-certs/20261009085141-ef89453832cd486a8e2d275de30c8a6c.png";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EventDbContext>();
        try
        {
            await db.Database.MigrateAsync(cancellationToken);
            try
            {
                await SeedPromoCodesAsync(db, cancellationToken);
                await SeedPackageOptionsAsync(db, cancellationToken);
                // Demo events + R2 media refresh tắt — tránh tạo lại event/banner sau wipe sạch.
                logger.LogInformation("Event schema migrated + promo/package seed checked.");
            }
            catch (Exception seedEx)
            {
                // Không kill host vì seed — API vẫn chạy (tránh "waiting for API" treo).
                logger.LogError(seedEx, "Event demo seed failed (API vẫn start).");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Event database migrate failed.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task SeedPromoCodesAsync(EventDbContext db, CancellationToken ct)
    {
        var expiresAt = DateTime.UtcNow.AddYears(2);
        (string Code, decimal Percent)[] seeds =
        [
            ("RACEDAY10", 0.10m),
            ("RACEDAY20", 0.20m),
            ("RACEDAY50", 0.50m),
            ("RACEDAY99", 0.99m), // tránh total = 0 (BE/PayOS yêu cầu > 0)
        ];

        var added = 0;
        foreach (var (code, percent) in seeds)
        {
            if (await db.PromoCodes.AnyAsync(x => x.Code == code, ct))
            {
                continue;
            }

            db.PromoCodes.Add(PromoCode.Create(code, percent, expiresAt));
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task SeedPackageOptionsAsync(EventDbContext db, CancellationToken ct)
    {
        if (!await db.PackageDurationOptions.AnyAsync(ct))
        {
            (string Name, int Months, decimal Price)[] durations =
            [
                ("1 tháng", 1, 1_000_000m),
                ("2 tháng", 2, 1_800_000m),
                ("3 tháng", 3, 2_500_000m),
                ("5 tháng", 5, 3_800_000m),
                ("6 tháng", 6, 4_500_000m),
                ("8 tháng", 8, 5_500_000m),
                ("10 tháng", 10, 6_500_000m),
                ("12 tháng", 12, 7_500_000m),
            ];
            foreach (var (name, months, price) in durations)
            {
                db.PackageDurationOptions.Add(PackageDurationOption.Create(name, months, price, months));
            }
        }

        if (!await db.PackageCapacityOptions.AnyAsync(ct))
        {
            (string Name, int Cap, decimal Fee)[] caps =
            [
                ("20 VĐV", 20, 0m),
                ("50 VĐV", 50, 200_000m),
                ("100 VĐV", 100, 500_000m),
                ("200 VĐV", 200, 1_200_000m),
                ("300 VĐV", 300, 2_000_000m),
                ("500 VĐV", 500, 3_500_000m),
                ("800 VĐV", 800, 5_500_000m),
                ("1.000 VĐV", 1000, 7_000_000m),
                ("2.000 VĐV", 2000, 12_000_000m),
            ];
            foreach (var (name, cap, fee) in caps)
            {
                db.PackageCapacityOptions.Add(PackageCapacityOption.Create(name, cap, fee, cap));
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedDemoEventsAsync(EventDbContext db, CancellationToken ct)
    {
        if (!await db.RaceEvents.AnyAsync(x => x.Id == DemoEventQuyNhonId, ct))
        {
            await CreateQuyNhonAsync(db, ct);
        }

        if (!await db.RaceEvents.AnyAsync(x => x.Id == DemoEventSapaId, ct))
        {
            await CreateSapaAsync(db, ct);
        }

        await EnsureQuyNhonDistancesAsync(db, ct);
    }

    /// <summary>
    /// Idempotent refresh for existing DBs (Docker volume giữ data cũ Đà Nẵng).
    /// Không đi qua UpdateDraftProfile (chỉ Draft/Rejected) — dùng ExecuteUpdate.
    /// </summary>
    private static async Task RefreshQuyNhonMediaAsync(EventDbContext db, CancellationToken ct)
    {
        await db.RaceEvents
            .Where(x => x.Id == DemoEventQuyNhonId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.Name, "VnExpress Marathon Quy Nhon 2026")
                    .SetProperty(e => e.Tagline, "Chạy đêm thành phố biển — VnExpress Marathon")
                    .SetProperty(e => e.BannerImage, BannerQuyNhon)
                    .SetProperty(e => e.Location, "Quy Nhơn, Bình Định")
                    .SetProperty(e => e.Description,
                        "Giải chạy Việt: VnExpress Marathon Quy Nhơn 2026. Cung đường ven biển, check-in QR, BIB điện tử. Seed FE/Admin với banner & hồ sơ trên R2.")
                    .SetProperty(e => e.TotalRunnersCap, 12000)
                    .SetProperty(e => e.EventDate, new DateOnly(2026, 6, 21))
                    .SetProperty(e => e.StartTime, new TimeOnly(4, 0))
                    .SetProperty(e => e.UpdatedAtUtc, DateTime.UtcNow),
                ct);

        // Owned GeoPoint / Weather — raw SQL (EF ExecuteUpdate limited on owned types)
        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE event.race_events
            SET coord_lat = 13.782967, coord_lng = 109.219663,
                weather_temp = '27°C', weather_condition = 'Đêm mát', weather_humidity = '78%',
                org_name = 'Ban tổ chức VnExpress Marathon Quy Nhơn',
                org_account_holder = COALESCE(org_account_holder, 'CONG TY RACE DAY'),
                org_account_number = COALESCE(org_account_number, '0123456789'),
                org_bank_name = COALESCE(org_bank_name, 'Vietcombank'),
                org_bank_branch = COALESCE(org_bank_branch, 'Quy Nhơn'),
                org_transfer_note = COALESCE(org_transfer_note, 'Giai ngan VnExpress Marathon Quy Nhon'),
                org_verified = true
            WHERE "Id" = {0}
            """,
            DemoEventQuyNhonId);

        await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM event.event_documents WHERE "EventId" = {0};
            INSERT INTO event.event_documents
              ("Id","EventId","DocumentKey","Title","FileName","FileUrl","Required","UploadedAtUtc")
            VALUES
              ('33333333-cccc-4ccc-8ccc-ccccccccccc1', {0}, 'plan',
               'Hồ sơ / kế hoạch tổ chức',
               '20261006082053-8e6f2c0a95404763801228bfaf40ec93.docx',
               {1}, true, TIMESTAMPTZ '2026-01-16 00:00:00+00'),
              ('33333333-cccc-4ccc-8ccc-cccccccccc10', {0}, 'permit',
               'Giấy phép / hồ sơ đính kèm',
               '20261006082053-8e6f2c0a95404763801228bfaf40ec93.docx',
               {1}, true, TIMESTAMPTZ '2026-01-16 00:00:00+00')
            ON CONFLICT ("Id") DO UPDATE SET
              "FileUrl" = EXCLUDED."FileUrl",
              "FileName" = EXCLUDED."FileName",
              "Title" = EXCLUDED."Title";
            """,
            DemoEventQuyNhonId,
            DocQuyNhon);

        await db.Registrations
            .Where(x => x.Id == Guid.Parse("44444444-dddd-4ddd-8ddd-ddddddddddd2")
                        || x.Id == Guid.Parse("44444444-dddd-4ddd-8ddd-ddddddddddd1"))
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(r => r.HealthCertFileName, "20261009085141-ef89453832cd486a8e2d275de30c8a6c.png")
                    .SetProperty(r => r.HealthCertUrl, HealthCert),
                ct);
    }

    private static async Task CreateQuyNhonAsync(EventDbContext db, CancellationToken ct)
    {
        var organizer = OrganizerInfo.Create(
            "Ban tổ chức VnExpress Marathon Quy Nhơn",
            "cuong.le@raceday.vn",
            "0904000004",
            "RaceDay Event Company",
            "https://raceday.vn",
            verified: true,
            accountHolder: "CONG TY RACE DAY",
            accountNumber: "0123456789",
            bankName: "Vietcombank",
            bankBranch: "Quy Nhơn",
            transferNote: "Giai ngan VnExpress Marathon Quy Nhon");

        var race = RaceEvent.CreateDraft(
            DemoOrganizerId,
            "VnExpress Marathon Quy Nhon 2026",
            "Quy Nhơn, Bình Định",
            new DateOnly(2026, 6, 21),
            new GeoPoint(13.782967, 109.219663),
            organizer,
            totalRunnersCap: 12000,
            tagline: "Chạy đêm thành phố biển — VnExpress Marathon",
            startTime: new TimeOnly(4, 0),
            description:
            "Giải chạy Việt: VnExpress Marathon Quy Nhơn 2026. Cung đường ven biển, check-in QR, BIB điện tử. Seed FE/Admin với banner & hồ sơ trên R2.",
            weather: null,
            bannerImage: BannerQuyNhon);

        SetId(race, DemoEventQuyNhonId);

        var distances = BuildQuyNhonDistances(race.Id);
        race.ReplaceDistances(distances);
        race.ReplaceDocuments([
            EventDocument.Create(
                race.Id, "plan", "Hồ sơ / kế hoạch tổ chức",
                "20261006082053-8e6f2c0a95404763801228bfaf40ec93.docx", DocQuyNhon, true),
            EventDocument.Create(
                race.Id, "permit", "Giấy phép / hồ sơ đính kèm",
                "20261006082053-8e6f2c0a95404763801228bfaf40ec93.docx", DocQuyNhon, true)
        ]);
        race.SubmitForApproval();
        race.Approve();
        race.OpenRegistration();

        db.RaceEvents.Add(race);
        await db.SaveChangesAsync(ct);
    }

    private static List<EventDistance> BuildQuyNhonDistances(Guid eventId)
    {
        var start = new NamedGeoPoint("Start Quy Nhơn", 13.782967, 109.219663);
        var finish = new NamedGeoPoint("Finish Quy Nhơn", 13.782967, 109.219663);

        var d5 = EventDistance.Create(eventId, "5K", "Fun Run 5K", 5, 15, 12, 0, 4000, 450_000m, "VND", "#22c55e", start, finish, "{}");
        SetId(d5, DemoDist5k);
        var d10 = EventDistance.Create(eventId, "10K", "City 10K", 10, 30, 18, 0, 3500, 650_000m, "VND", "#0ea5e9", start, finish, "{}");
        SetId(d10, DemoDist10k);
        var d21 = EventDistance.Create(eventId, "21K", "Half Marathon", 21.1, 80, 35, 0, 2500, 990_000m, "VND", "#F59E0B", start, finish, "{}");
        SetId(d21, DemoDist21k);
        var d42 = EventDistance.Create(eventId, "42K", "Full Marathon", 42.195, 150, 40, 0, 2000, 1_450_000m, "VND", "#EF4444", start, finish, "{}");
        SetId(d42, DemoDist42k);
        return [d5, d10, d21, d42];
    }

    private static async Task EnsureQuyNhonDistancesAsync(EventDbContext db, CancellationToken ct)
    {
        if (!await db.RaceEvents.AnyAsync(x => x.Id == DemoEventQuyNhonId, ct))
        {
            return;
        }

        // Raw SQL — tránh EF OwnsOne StartLocation null khi Add Distances tách khỏi aggregate.
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO event.event_distances (
                "Id", "EventId", "Type", "Name", "DistanceKm",
                "ElevationGainM", "MaxElevationM", "MinElevationM",
                "Capacity", "RegisteredCount", "Price", "Currency", "Color",
                start_name, start_lat, start_lng, finish_name, finish_lat, finish_lng,
                "RouteGeometryJson"
            ) VALUES
            ('22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb1', {0}, '5K', 'Fun Run 5K', 5,
             15, 12, 0, 4000, 0, 450000, 'VND', '#22c55e',
             'Start Quy Nhơn', 13.782967, 109.219663, 'Finish Quy Nhơn', 13.782967, 109.219663, '{{}}'::jsonb),
            ('22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb2', {0}, '10K', 'City 10K', 10,
             30, 18, 0, 3500, 0, 650000, 'VND', '#0ea5e9',
             'Start Quy Nhơn', 13.782967, 109.219663, 'Finish Quy Nhơn', 13.7855, 109.221, '{{}}'::jsonb),
            ('22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb4', {0}, '21K', 'Half Marathon', 21.1,
             80, 35, 0, 2500, 0, 990000, 'VND', '#F59E0B',
             'Start Quy Nhơn', 13.782967, 109.219663, 'Finish Quy Nhơn', 13.782967, 109.219663, '{{}}'::jsonb),
            ('22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb5', {0}, '42K', 'Full Marathon', 42.195,
             150, 40, 0, 2000, 0, 1450000, 'VND', '#EF4444',
             'Start Quy Nhơn', 13.782967, 109.219663, 'Finish Quy Nhơn', 13.782967, 109.219663, '{{}}'::jsonb)
            ON CONFLICT ("Id") DO UPDATE SET
                "Name" = EXCLUDED."Name",
                "Price" = EXCLUDED."Price",
                "Type" = EXCLUDED."Type",
                "DistanceKm" = EXCLUDED."DistanceKm",
                "Capacity" = EXCLUDED."Capacity",
                "Color" = EXCLUDED."Color",
                start_name = EXCLUDED.start_name,
                start_lat = EXCLUDED.start_lat,
                start_lng = EXCLUDED.start_lng,
                finish_name = EXCLUDED.finish_name,
                finish_lat = EXCLUDED.finish_lat,
                finish_lng = EXCLUDED.finish_lng
            """,
            DemoEventQuyNhonId);
    }

    private static async Task CreateSapaAsync(EventDbContext db, CancellationToken ct)
    {
        const string bannerSapa =
            "https://images.unsplash.com/photo-1476480862126-209bfaa8edc8?auto=format&fit=crop&w=1600&q=80";

        var organizerSapa = OrganizerInfo.Create(
            "Ban tổ chức RaceDay Sa Pa",
            "cuong.le@raceday.vn",
            "0904000004",
            "RaceDay Event Company",
            "https://raceday.vn",
            verified: true);

        var sapa = RaceEvent.CreateDraft(
            DemoOrganizerId,
            "Vietnam Mountain Marathon — Sa Pa (Demo)",
            "Sa Pa, Lào Cai",
            new DateOnly(2026, 12, 14),
            new GeoPoint(22.3364, 103.8438),
            organizerSapa,
            totalRunnersCap: 1900,
            tagline: "Trail núi Hoàng Liên Sơn",
            startTime: new TimeOnly(6, 0),
            description: "Demo trail Sa Pa — có bannerImage để FE card/carousel hiển thị ảnh thật.",
            weather: null,
            bannerImage: bannerSapa);

        SetId(sapa, DemoEventSapaId);
        var s21 = EventDistance.Create(
            sapa.Id, "21K", "Cloud Valley", 21, 980, 2150, 1450, 800, 900_000m, "VND", "#F59E0B",
            new NamedGeoPoint("Sa Pa Square", 22.3364, 103.8438),
            new NamedGeoPoint("Sa Pa Square", 22.3364, 103.8438),
            "{}");
        SetId(s21, DemoDistSapa21k);
        sapa.ReplaceDistances([s21]);
        sapa.ReplaceDocuments([
            EventDocument.Create(
                sapa.Id, "plan", "Hồ sơ / kế hoạch tổ chức",
                "20261006082053-8e6f2c0a95404763801228bfaf40ec93.docx", DocQuyNhon, true)
        ]);
        sapa.SubmitForApproval();
        sapa.Approve();
        sapa.OpenRegistration();
        db.RaceEvents.Add(sapa);
        await db.SaveChangesAsync(ct);
    }

    private static void SetId(object entity, Guid id)
    {
        var prop = entity.GetType().GetProperty("Id")
                   ?? throw new InvalidOperationException("Entity missing Id");
        prop.SetValue(entity, id);
    }
}
