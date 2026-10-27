using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;
using RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Persistence;

public class EmergencyResponseModelTests
{
    [Fact]
    public void SourceSosAlertId_Is_Unique_And_Has_No_Foreign_Key()
    {
        var options = new DbContextOptionsBuilder<RaceMonitoringDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=raceday_model_check;Username=model;Password=model")
            .Options;
        using var db = new RaceMonitoringDbContext(options);
        var entity = db.Model.FindEntityType(typeof(EmergencyResponse));
        Assert.NotNull(entity);

        var index = entity.GetIndexes()
            .Single(item => item.Properties.Any(property => property.Name == nameof(EmergencyResponse.SourceSosAlertId)));
        Assert.True(index.IsUnique);
        Assert.DoesNotContain(
            entity.GetForeignKeys(),
            foreignKey => foreignKey.Properties.Any(property => property.Name == nameof(EmergencyResponse.SourceSosAlertId)));
        Assert.NotNull(entity.FindProperty(nameof(EmergencyResponse.CancelledAtUtc)));
        Assert.NotNull(entity.FindProperty(nameof(EmergencyResponse.CancellationReason)));
    }

    [Fact]
    public void Assignment_Response_And_Resource_Pair_Is_Unique()
    {
        var options = new DbContextOptionsBuilder<RaceMonitoringDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=raceday_model_check;Username=model;Password=model")
            .Options;
        using var db = new RaceMonitoringDbContext(options);
        var entity = db.Model.FindEntityType(typeof(EmergencyAssignment));
        Assert.NotNull(entity);

        var index = entity.GetIndexes().Single(item =>
            item.GetDatabaseName() == "IX_emergency_assignments_ResponseId_ResourceId");
        Assert.True(index.IsUnique);
        Assert.Equal(
            [nameof(EmergencyAssignment.EmergencyResponseId), nameof(EmergencyAssignment.EmergencyResourceId)],
            index.Properties.Select(property => property.Name).ToArray());
    }
}
