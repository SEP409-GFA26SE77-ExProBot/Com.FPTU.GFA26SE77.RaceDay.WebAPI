using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.UnitTests.Domain;

public class CheckpointCourseSyncTests
{
    private static EventDistance Distance() => EventDistance.Create(
        Guid.NewGuid(), "5KM", 5, 100, 0m, type: "5KM",
        checkpoints:
        [
            ("cp-0", "Start", 0, 10.0, 106.0, CheckpointType.Start, null, null),
            ("cp-2", "CP 2km", 2, 10.1, 106.1, CheckpointType.Water, "06:45", 12),
            ("cp-5", "Finish", 5, 10.2, 106.2, CheckpointType.Finish, null, null)
        ]);

    private static CourseCheckpointDraft Draft(string id, string name, double km, CheckpointType type, string? cutoff = null)
        => new(id, name, km, 10.5, 106.5, type, cutoff);

    private static readonly IReadOnlySet<Guid> NoData = new HashSet<Guid>();

    [Fact]
    public void SyncCheckpointsFromCourse_MatchesByExternalId_UpdatesAddsAndRemoves()
    {
        var distance = Distance();
        var cp2 = distance.Checkpoints.Single(c => c.ExternalId == "cp-2");
        var finishId = distance.Checkpoints.Single(c => c.ExternalId == "cp-5").Id;

        var outcome = distance.SyncCheckpointsFromCourse(
        [
            Draft("cp-0", "Xuất phát", 0, CheckpointType.Start),
            Draft("cp-2", "CP 2.1km", 2.1, CheckpointType.Aid),
            Draft("cp-3", "CP 3km", 3, CheckpointType.Water),
            Draft("cp-5b", "Đích", 5.07, CheckpointType.Finish)
        ], NoData);

        Assert.Equal(2, outcome.Updated);
        Assert.Equal(2, outcome.Added);
        Assert.Equal(1, outcome.Removed);
        Assert.DoesNotContain(distance.Checkpoints, c => c.Id == finishId);
        Assert.Same(cp2, distance.Checkpoints.Single(c => c.ExternalId == "cp-2"));
        Assert.Equal("CP 2.1km", cp2.Name);
        Assert.Equal(2.1, cp2.Km);
        Assert.Equal(CheckpointType.Aid, cp2.Type);
        Assert.Equal("06:45", cp2.Cutoff);
        Assert.Equal(12, cp2.Elevation);
        Assert.Equal(1, cp2.SortOrder);
        Assert.Equal(["cp-3", "cp-5b"], outcome.NewCheckpoints.Select(c => c.ExternalId));
        Assert.All(outcome.NewCheckpoints, c => Assert.Equal(distance.Id, c.DistanceId));
    }

    [Fact]
    public void SyncCheckpointsFromCourse_NewCutoff_ReplacesStoredValue()
    {
        var distance = Distance();

        distance.SyncCheckpointsFromCourse(
        [
            Draft("cp-0", "Start", 0, CheckpointType.Start),
            Draft("cp-2", "CP 2km", 2, CheckpointType.Water, "07:00"),
            Draft("cp-5", "Finish", 5, CheckpointType.Finish)
        ], NoData);

        Assert.Equal("07:00", distance.Checkpoints.Single(c => c.ExternalId == "cp-2").Cutoff);
    }

    [Fact]
    public void SyncCheckpointsFromCourse_IdChangedButSameTypeAndKm_KeepsRowAndTakesNewId()
    {
        var distance = Distance();
        var startId = distance.Checkpoints.Single(c => c.ExternalId == "cp-0").Id;

        var outcome = distance.SyncCheckpointsFromCourse(
        [
            Draft("map-start", "Start", 0.0004, CheckpointType.Start),
            Draft("cp-2", "CP 2km", 2, CheckpointType.Water),
            Draft("cp-5", "Finish", 5, CheckpointType.Finish)
        ], new HashSet<Guid> { startId });

        Assert.Equal(0, outcome.Removed);
        Assert.Equal(0, outcome.Added);
        Assert.Equal("map-start", distance.Checkpoints.Single(c => c.Id == startId).ExternalId);
    }

    [Fact]
    public void SyncCheckpointsFromCourse_RemovingCheckpointWithData_ThrowsAndChangesNothing()
    {
        var distance = Distance();
        var cp2 = distance.Checkpoints.Single(c => c.ExternalId == "cp-2");

        var error = Assert.Throws<DomainException>(() => distance.SyncCheckpointsFromCourse(
        [
            Draft("cp-0", "Start renamed", 0, CheckpointType.Start),
            Draft("cp-5", "Finish", 5, CheckpointType.Finish)
        ], new HashSet<Guid> { cp2.Id }));

        Assert.Contains("CP 2km", error.Message);
        Assert.Equal(3, distance.Checkpoints.Count);
        Assert.Equal("Start", distance.Checkpoints.Single(c => c.ExternalId == "cp-0").Name);
    }

    [Fact]
    public void SyncCheckpointsFromCourse_RemovingCheckpointWithoutData_Removes()
    {
        var distance = Distance();

        var outcome = distance.SyncCheckpointsFromCourse(
        [
            Draft("cp-0", "Start", 0, CheckpointType.Start),
            Draft("cp-5", "Finish", 5, CheckpointType.Finish)
        ], NoData);

        Assert.Equal(1, outcome.Removed);
        Assert.DoesNotContain(distance.Checkpoints, c => c.ExternalId == "cp-2");
    }

    [Theory]
    [InlineData("", "Mã checkpoint")]
    [InlineData("cp-0", "bị trùng")]
    public void SyncCheckpointsFromCourse_InvalidOrDuplicateIds_Throw(string secondId, string expected)
    {
        var distance = Distance();

        var error = Assert.Throws<DomainException>(() => distance.SyncCheckpointsFromCourse(
        [
            Draft("cp-0", "Start", 0, CheckpointType.Start),
            Draft(secondId, "Other", 1, CheckpointType.Water)
        ], NoData));

        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void SyncCheckpointsFromCourse_CutoffTooLong_Throws()
    {
        var distance = Distance();

        Assert.Throws<DomainException>(() => distance.SyncCheckpointsFromCourse(
            [Draft("cp-0", "Start", 0, CheckpointType.Start, new string('9', 21))],
            NoData));
    }
}
