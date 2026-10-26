using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.RaceMonitoring.Domain;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence;

public sealed class RaceMonitoringDataSeeder(
    IServiceScopeFactory scopeFactory,
    ILogger<RaceMonitoringDataSeeder> logger) : IHostedService
{
    // Khớp deploy/sql/raceday_init.sql — không FK sang schema event.
    private static readonly Guid AnHcmcRegistrationId = Guid.Parse("44444444-dddd-4ddd-8ddd-ddddddddddd1");
    private static readonly Guid BinhDaNangRegistrationId = Guid.Parse("44444444-dddd-4ddd-8ddd-ddddddddddd2");
    private static readonly Guid AnHanoiRegistrationId = Guid.Parse("44444444-dddd-4ddd-8ddd-ddddddddddd3");

    private static readonly Guid HcmcStartId = Guid.Parse("55555555-cccc-4ccc-8ccc-ccccccccccc1");
    private static readonly Guid HcmcAidId = Guid.Parse("55555555-cccc-4ccc-8ccc-ccccccccccc2");
    private static readonly Guid HcmcFinishId = Guid.Parse("55555555-cccc-4ccc-8ccc-ccccccccccc3");
    private static readonly Guid DaNangStartId = Guid.Parse("55555555-cccc-4ccc-8ccc-ccccccccccc4");
    private static readonly Guid DaNangWaterId = Guid.Parse("55555555-cccc-4ccc-8ccc-ccccccccccc5");
    private static readonly Guid HanoiStartId = Guid.Parse("55555555-cccc-4ccc-8ccc-ccccccccccc7");
    private static readonly Guid HanoiAidId = Guid.Parse("55555555-cccc-4ccc-8ccc-ccccccccccc8");

    private static readonly Guid AnHcmcLocation1Id = Guid.Parse("77777777-aaaa-4aaa-8aaa-aaaaaaaaaaa1");
    private static readonly Guid AnHcmcLocation2Id = Guid.Parse("77777777-aaaa-4aaa-8aaa-aaaaaaaaaaa2");
    private static readonly Guid AnHcmcLocation3Id = Guid.Parse("77777777-aaaa-4aaa-8aaa-aaaaaaaaaaa3");
    private static readonly Guid BinhLocation1Id = Guid.Parse("77777777-aaaa-4aaa-8aaa-aaaaaaaaaaa4");
    private static readonly Guid BinhLocation2Id = Guid.Parse("77777777-aaaa-4aaa-8aaa-aaaaaaaaaaa5");
    private static readonly Guid AnHanoiLocation1Id = Guid.Parse("77777777-aaaa-4aaa-8aaa-aaaaaaaaaaa6");
    private static readonly Guid AnHanoiLocation2Id = Guid.Parse("77777777-aaaa-4aaa-8aaa-aaaaaaaaaaa7");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RaceMonitoringDbContext>();
        try
        {
            await db.Database.MigrateAsync(cancellationToken);
            await SeedAsync(db, cancellationToken);
            logger.LogInformation("RaceMonitoring schema migrated + demo seed checked.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RaceMonitoring database migrate/seed failed.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task SeedAsync(RaceMonitoringDbContext db, CancellationToken ct)
    {
        if (await db.RunnerLocations.AnyAsync(x => x.Id == AnHcmcLocation1Id, ct))
        {
            return;
        }

        var anHcmc1 = Location(AnHcmcLocation1Id, AnHcmcRegistrationId, 10.7734, 106.7033, Utc(2026, 3, 15, 11, 5, 12), Utc(2026, 3, 15, 11, 5, 14));
        var anHcmc2 = Location(AnHcmcLocation2Id, AnHcmcRegistrationId, 10.7746, 106.7041, Utc(2026, 3, 15, 11, 22, 40), Utc(2026, 3, 15, 11, 22, 43));
        var anHcmc3 = Location(AnHcmcLocation3Id, AnHcmcRegistrationId, 10.7758, 106.7022, Utc(2026, 3, 15, 11, 48, 5), Utc(2026, 3, 15, 11, 48, 8));
        var binh1 = Location(BinhLocation1Id, BinhDaNangRegistrationId, 16.0602, 108.2494, Utc(2026, 11, 21, 22, 36, 10), Utc(2026, 11, 21, 22, 36, 12));
        var binh2 = Location(BinhLocation2Id, BinhDaNangRegistrationId, 16.0675, 108.2572, Utc(2026, 11, 21, 23, 5, 40), Utc(2026, 11, 21, 23, 5, 44));
        var anHanoi1 = Location(AnHanoiLocation1Id, AnHanoiRegistrationId, 21.0590, 105.8235, Utc(2026, 6, 6, 22, 40, 0), Utc(2026, 6, 6, 22, 40, 3));
        var anHanoi2 = Location(AnHanoiLocation2Id, AnHanoiRegistrationId, 21.0672, 105.8294, Utc(2026, 6, 6, 23, 30, 20), Utc(2026, 6, 6, 23, 30, 24));

        db.RunnerLocations.AddRange(anHcmc1, anHcmc2, anHcmc3, binh1, binh2, anHanoi1, anHanoi2);
        await db.SaveChangesAsync(ct);

        db.CheckpointScans.AddRange(
            Scan(Guid.Parse("88888888-bbbb-4bbb-8bbb-bbbbbbbbbbb1"), AnHcmcRegistrationId, HcmcStartId, ScanMethod.RFID, Utc(2026, 3, 15, 11, 4, 50)),
            Scan(Guid.Parse("88888888-bbbb-4bbb-8bbb-bbbbbbbbbbb2"), AnHcmcRegistrationId, HcmcAidId, ScanMethod.RFID, Utc(2026, 3, 15, 11, 23, 2)),
            Scan(Guid.Parse("88888888-bbbb-4bbb-8bbb-bbbbbbbbbbb3"), AnHcmcRegistrationId, HcmcFinishId, ScanMethod.RFID, Utc(2026, 3, 15, 11, 49, 10)),
            Scan(Guid.Parse("88888888-bbbb-4bbb-8bbb-bbbbbbbbbbb4"), BinhDaNangRegistrationId, DaNangStartId, ScanMethod.RFID, Utc(2026, 11, 21, 22, 35, 40)),
            Scan(Guid.Parse("88888888-bbbb-4bbb-8bbb-bbbbbbbbbbb5"), BinhDaNangRegistrationId, DaNangWaterId, ScanMethod.Manual, Utc(2026, 11, 21, 23, 6, 15)),
            Scan(Guid.Parse("88888888-bbbb-4bbb-8bbb-bbbbbbbbbbb6"), AnHanoiRegistrationId, HanoiStartId, ScanMethod.RFID, Utc(2026, 6, 6, 22, 39, 0)),
            Scan(Guid.Parse("88888888-bbbb-4bbb-8bbb-bbbbbbbbbbb7"), AnHanoiRegistrationId, HanoiAidId, ScanMethod.RFID, Utc(2026, 6, 6, 23, 32, 10)));

        var active = SOSAlert.Create(
            BinhDaNangRegistrationId,
            Utc(2026, 11, 21, 23, 10, 0),
            BinhLocation2Id,
            16.0675,
            108.2572);
        SetId(active, Guid.Parse("99999999-cccc-4ccc-8ccc-ccccccccccc1"));

        var acknowledged = SOSAlert.Create(
            AnHcmcRegistrationId,
            Utc(2026, 3, 15, 11, 30, 0),
            AnHcmcLocation2Id);
        acknowledged.Acknowledge();
        SetId(acknowledged, Guid.Parse("99999999-cccc-4ccc-8ccc-ccccccccccc2"));

        var resolved = SOSAlert.Create(
            AnHanoiRegistrationId,
            Utc(2026, 6, 6, 23, 40, 0),
            AnHanoiLocation2Id,
            21.0672,
            105.8294);
        resolved.Resolve(Utc(2026, 6, 7, 0, 5, 0));
        SetId(resolved, Guid.Parse("99999999-cccc-4ccc-8ccc-ccccccccccc3"));

        db.SOSAlerts.AddRange(active, acknowledged, resolved);
        await db.SaveChangesAsync(ct);
    }

    private static RunnerLocation Location(
        Guid id,
        Guid registrationId,
        double latitude,
        double longitude,
        DateTime recordedAtUtc,
        DateTime receivedAtUtc)
    {
        var location = RunnerLocation.Create(registrationId, latitude, longitude, recordedAtUtc, receivedAtUtc);
        SetId(location, id);
        return location;
    }

    private static CheckpointScan Scan(
        Guid id,
        Guid registrationId,
        Guid checkpointId,
        ScanMethod scanMethod,
        DateTime scannedAtUtc)
    {
        var scan = CheckpointScan.Create(registrationId, checkpointId, scanMethod, scannedAtUtc);
        SetId(scan, id);
        return scan;
    }

    private static void SetId(object entity, Guid id)
    {
        var prop = entity.GetType().GetProperty("Id")
                   ?? throw new InvalidOperationException("Entity missing Id");
        prop.SetValue(entity, id);
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute, int second) =>
        new(year, month, day, hour, minute, second, DateTimeKind.Utc);
}
