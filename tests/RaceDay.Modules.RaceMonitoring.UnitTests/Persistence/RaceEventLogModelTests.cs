using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Persistence;

public class RaceEventLogModelTests
{
    [Fact]
    public void Logs_Are_Indexed_For_Replay_Without_One_Pass_Per_Checkpoint()
    {
        using var db = new RaceMonitoringDbContext(Options());
        var log = db.Model.FindEntityType(typeof(RaceEventLog));
        Assert.NotNull(log);
        Assert.Contains(log.GetIndexes(), index => index.GetDatabaseName() == "IX_race_event_logs_EventId_Sequence");
        var source = log.GetIndexes().Single(index => index.GetDatabaseName() == "IX_race_event_logs_Source");
        Assert.True(source.IsUnique);
        Assert.Equal(ValueGenerated.OnAdd, log.FindProperty(nameof(RaceEventLog.Sequence))!.ValueGenerated);
        Assert.DoesNotContain(
            log.GetForeignKeys(),
            foreignKey => foreignKey.Properties.Any(property => property.Name == nameof(RaceEventLog.RegistrationId)));

        var scan = db.Model.FindEntityType(typeof(CheckpointScan));
        Assert.NotNull(scan);
        Assert.DoesNotContain(
            scan.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Select(property => property.Name).OrderBy(name => name).SequenceEqual(
                    new[] { nameof(CheckpointScan.CheckpointId), nameof(CheckpointScan.RegistrationId) }));
    }

    private static DbContextOptions<RaceMonitoringDbContext> Options()
        => new DbContextOptionsBuilder<RaceMonitoringDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=raceday_model_check;Username=model;Password=model")
            .Options;
}
