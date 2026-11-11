using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;
using RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Persistence;

public class EmergencyResourcePersistenceTests
{
    [Fact]
    public void Event_Resource_Code_Index_Is_Unique_Per_Event_And_Separate_From_No_Event_Code()
    {
        using var db = new RaceMonitoringDbContext(Options());
        var entity = db.Model.FindEntityType(typeof(EmergencyResource));
        Assert.NotNull(entity);

        var perEvent = entity.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(EmergencyResource.EventId), nameof(EmergencyResource.ResourceCode)]));
        Assert.True(perEvent.IsUnique);
        Assert.Equal("IX_emergency_resources_EventId_ResourceCode", perEvent.GetDatabaseName());
        Assert.Equal("\"EventId\" IS NOT NULL", perEvent.GetFilter());

        var noEvent = entity.GetIndexes().Single(index =>
            index.GetDatabaseName() == "IX_emergency_resources_ResourceCode_NoEvent");
        Assert.True(noEvent.IsUnique);
        Assert.Equal("\"EventId\" IS NULL", noEvent.GetFilter());
        Assert.Equal([nameof(EmergencyResource.ResourceCode)], noEvent.Properties.Select(property => property.Name).ToArray());
    }

    [Fact]
    public void Add_Tracks_The_Resource_As_Added_Without_Saving()
    {
        using var db = new RaceMonitoringDbContext(Options());
        var resource = EmergencyResource.Create(
            Guid.NewGuid(),
            "MED-1",
            EmergencyResourceType.MedicalTeam,
            "Đội y tế",
            EmergencyResourceStatus.Available);

        new EmergencyResourceRepository(db).Add(resource);

        Assert.Equal(EntityState.Added, db.Entry(resource).State);
    }

    [Fact]
    public async Task Save_Maps_Per_Event_Resource_Code_Only_From_Structured_Unique_Violation()
    {
        var perEvent = PerEventResourceCodeIndexName();

        var mapped = await Assert.ThrowsAsync<DomainException>(() =>
            SaveAsync(StructuredViolation(PostgresErrorCodes.UniqueViolation, perEvent, perEvent)));
        Assert.Equal("Mã nguồn lực đã tồn tại trong giải.", mapped.Message);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            SaveAsync(StructuredViolation(PostgresErrorCodes.ForeignKeyViolation, perEvent, perEvent)));
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            SaveAsync(StructuredViolation(
                PostgresErrorCodes.UniqueViolation,
                "IX_emergency_resources_ResourceCode_NoEvent",
                perEvent)));
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            SaveAsync(StructuredViolation(PostgresErrorCodes.UniqueViolation, "IX_some_other_constraint", perEvent)));
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            SaveAsync(Duplicate(perEvent)));
    }

    [Fact]
    public async Task Save_Keeps_Existing_Assignment_And_Sos_Conflict_Behavior()
    {
        var assignmentError = await Assert.ThrowsAsync<DomainException>(() =>
            SaveAsync(Duplicate("IX_emergency_assignments_ResponseId_ResourceId")));
        Assert.Equal("Nguồn lực này đã nằm trong danh sách phân công của hồ sơ.", assignmentError.Message);

        var saved = await SaveAsync(Duplicate("IX_emergency_responses_SourceSosAlertId"));
        Assert.Equal(0, saved);
    }

    private static DbContextOptions<RaceMonitoringDbContext> Options(Exception? saveError = null)
    {
        var builder = new DbContextOptionsBuilder<RaceMonitoringDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=raceday_model_check;Username=model;Password=model");
        if (saveError is not null)
        {
            builder.AddInterceptors(new ThrowOnSaveInterceptor(saveError));
        }

        return builder.Options;
    }

    private static async Task<int> SaveAsync(Exception error)
    {
        await using var db = new RaceMonitoringDbContext(Options(error));
        db.EmergencyResources.Add(EmergencyResource.Create(
            Guid.NewGuid(),
            "MED-1",
            EmergencyResourceType.MedicalTeam,
            "Đội y tế",
            EmergencyResourceStatus.Available));
        return await new RaceMonitoringUnitOfWork(db).SaveChangesAsync(CancellationToken.None);
    }

    private static string PerEventResourceCodeIndexName()
    {
        using var db = new RaceMonitoringDbContext(Options());
        var name = db.Model.FindEntityType(typeof(EmergencyResource))!
            .GetIndexes()
            .Single(index =>
                index.IsUnique
                && index.Properties.Select(property => property.Name).SequenceEqual(
                    [nameof(EmergencyResource.EventId), nameof(EmergencyResource.ResourceCode)]))
            .GetDatabaseName();
        Assert.False(string.IsNullOrWhiteSpace(name));
        return name!;
    }

    // In-memory Npgsql exception. The save interceptor throws it before a connection is opened.
    private static DbUpdateException StructuredViolation(string sqlState, string? constraintName, string messageName) =>
        new(
            "update failed",
            new PostgresException(
                $"duplicate key value violates unique constraint \"{messageName}\"",
                "ERROR",
                "ERROR",
                sqlState,
                constraintName: constraintName));

    private static DbUpdateException Duplicate(string constraintName) =>
        new("update failed", new Exception($"duplicate key value violates unique constraint \"{constraintName}\""));

    private sealed class ThrowOnSaveInterceptor(Exception error) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
            => throw error;
    }
}
