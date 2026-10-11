using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Events;
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
        services.AddScoped<IEventUnitOfWork, EventUnitOfWork>();
        services.AddScoped<RaceDay.Modules.Event.Contracts.Registrations.IRegistrationCheckoutInfoQuery,
            RaceDay.Modules.Event.Infrastructure.Registrations.RegistrationCheckoutInfoQuery>();
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
    // Stable demo ids — FE / smoke can deep-link.
    private static readonly Guid DemoOrganizerId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"); // cuong.le@raceday.vn
    private static readonly Guid DemoEventDaNangId = Guid.Parse("11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa1");
    private static readonly Guid DemoEventSapaId = Guid.Parse("11111111-aaaa-4aaa-8aaa-aaaaaaaaaaa2");
    private static readonly Guid DemoDistDaNang5k = Guid.Parse("22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb1");
    private static readonly Guid DemoDistDaNang10k = Guid.Parse("22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb2");
    private static readonly Guid DemoDistSapa21k = Guid.Parse("22222222-bbbb-4bbb-8bbb-bbbbbbbbbbb3");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EventDbContext>();
        try
        {
            await db.Database.MigrateAsync(cancellationToken);
            await SeedDemoEventsAsync(db, cancellationToken);
            logger.LogInformation("Event schema migrated + demo seed checked.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Event database migrate/seed failed.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task SeedDemoEventsAsync(EventDbContext db, CancellationToken ct)
    {
        if (await db.RaceEvents.AnyAsync(x => x.Id == DemoEventDaNangId, ct))
        {
            return;
        }

        var organizerDaNang = OrganizerInfo.Create(
            "Ban tổ chức RaceDay Đà Nẵng",
            "cuong.le@raceday.vn",
            "0904000004",
            "RaceDay Event Company",
            "https://raceday.vn",
            verified: true);

        var organizerSapa = OrganizerInfo.Create(
            "Ban tổ chức RaceDay Sa Pa",
            "cuong.le@raceday.vn",
            "0904000004",
            "RaceDay Event Company",
            "https://raceday.vn",
            verified: true);

        // Public CDN images (Unsplash) — FE bannerImage expects absolute URL when not under /public
        const string bannerDaNang =
            "https://images.unsplash.com/photo-1452626635019-14babd4f4a0b?auto=format&fit=crop&w=1600&q=80";
        const string bannerSapa =
            "https://images.unsplash.com/photo-1476480862126-209bfaa8edc8?auto=format&fit=crop&w=1600&q=80";

        var daNang = RaceEvent.CreateDraft(
            DemoOrganizerId,
            "RaceDay Coastal Run Đà Nẵng 2026",
            "Biển Mỹ Khê, Đà Nẵng",
            new DateOnly(2026, 11, 22),
            new GeoPoint(16.05995, 108.24909),
            organizerDaNang,
            totalRunnersCap: 3200,
            tagline: "Chạy cùng biển xanh — mở đăng ký ngay",
            startTime: new TimeOnly(5, 30),
            description:
            "Giải chạy duyên hải Đà Nẵng: cung đường ven biển Mỹ Khê, check-in QR, BIB điện tử và kit runner. Demo seed cho FE test.",
            weather: new WeatherInfo("26°C", "Nắng nhẹ", "70%"),
            bannerImage: bannerDaNang);

        SetId(daNang, DemoEventDaNangId);

        var d5 = EventDistance.Create(
            daNang.Id, "5K", "Fun Run 5K", 5, 20, 15, 0, 1500, 199_000, "VND", "#22c55e",
            new NamedGeoPoint("Start Mỹ Khê", 16.05995, 108.24909),
            new NamedGeoPoint("Finish Mỹ Khê", 16.0612, 108.251),
            "{}");
        SetId(d5, DemoDistDaNang5k);

        var d10 = EventDistance.Create(
            daNang.Id, "10K", "Coastal 10K", 10, 40, 25, 0, 1200, 349_000, "VND", "#0ea5e9",
            new NamedGeoPoint("Start Mỹ Khê", 16.05995, 108.24909),
            new NamedGeoPoint("Finish Sơn Trà", 16.08, 108.27),
            "{}");
        SetId(d10, DemoDistDaNang10k);

        daNang.ReplaceDistances([d5, d10]);
        daNang.ReplaceDocuments([
            EventDocument.Create(daNang.Id, "plan", "Kế hoạch tổ chức", "plan.pdf", "https://example.com/demo/plan.pdf", true)
        ]);
        daNang.SubmitForApproval();
        daNang.Approve();
        daNang.OpenRegistration();

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
            weather: new WeatherInfo("14°C", "Sương mù", "85%"),
            bannerImage: bannerSapa);

        SetId(sapa, DemoEventSapaId);
        var s21 = EventDistance.Create(
            sapa.Id, "21K", "Cloud Valley", 21, 980, 2150, 1450, 800, 900_000, "VND", "#F59E0B",
            new NamedGeoPoint("Sa Pa Square", 22.3364, 103.8438),
            new NamedGeoPoint("Sa Pa Square", 22.3364, 103.8438),
            "{}");
        SetId(s21, DemoDistSapa21k);
        sapa.ReplaceDistances([s21]);
        sapa.ReplaceDocuments([
            EventDocument.Create(sapa.Id, "plan", "Kế hoạch tổ chức", "plan.pdf", "https://example.com/demo/sapa-plan.pdf", true)
        ]);
        sapa.SubmitForApproval();
        sapa.Approve();
        sapa.OpenRegistration();

        db.RaceEvents.AddRange(daNang, sapa);
        await db.SaveChangesAsync(ct);
    }

    private static void SetId(object entity, Guid id)
    {
        var prop = entity.GetType().GetProperty("Id")
                   ?? throw new InvalidOperationException("Entity missing Id");
        prop.SetValue(entity, id);
    }
}
