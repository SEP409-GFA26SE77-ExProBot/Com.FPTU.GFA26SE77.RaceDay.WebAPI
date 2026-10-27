using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Domain;

public class EmergencyAssignmentTests
{
    private static readonly DateTime AssignedAt = new(2026, 10, 27, 1, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 10, 27, 2, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Advance_Records_Each_Timestamp_On_The_Same_Row()
    {
        var assignment = Assigned();

        assignment.Advance(EmergencyAssignmentStatus.Accepted, Later);
        Assert.Equal(EmergencyAssignmentStatus.Accepted, assignment.Status);
        Assert.Equal(Later, assignment.AcceptedAtUtc);

        var arrivedAt = Later.AddMinutes(10);
        assignment.Advance(EmergencyAssignmentStatus.Arrived, arrivedAt);
        Assert.Equal(EmergencyAssignmentStatus.Arrived, assignment.Status);
        Assert.Equal(arrivedAt, assignment.ArrivedAtUtc);

        var completedAt = Later.AddMinutes(20);
        assignment.Advance(EmergencyAssignmentStatus.Completed, completedAt);
        Assert.Equal(EmergencyAssignmentStatus.Completed, assignment.Status);
        Assert.Equal(completedAt, assignment.CompletedAtUtc);
        Assert.Equal(AssignedAt, assignment.AssignedAtUtc);
    }

    [Fact]
    public void Advance_Rejects_Skipping_Accepted()
    {
        var assignment = Assigned();

        var ex = Assert.Throws<DomainException>(() =>
            assignment.Advance(EmergencyAssignmentStatus.Arrived, Later));

        Assert.Equal("Không thể chuyển phân công ở trạng thái hiện tại.", ex.Message);
        Assert.Equal(EmergencyAssignmentStatus.Assigned, assignment.Status);
        Assert.Null(assignment.ArrivedAtUtc);
    }

    [Fact]
    public void Advance_Rejects_Cancelled_As_A_Target()
    {
        var assignment = Assigned();

        var ex = Assert.Throws<DomainException>(() =>
            assignment.Advance(EmergencyAssignmentStatus.Cancelled, Later));

        Assert.Equal(EmergencyAssignmentStatus.Assigned, assignment.Status);
        Assert.Equal("Không thể chuyển phân công ở trạng thái hiện tại.", ex.Message);
    }

    [Fact]
    public void Response_Cancellation_Completes_Open_Assignment_Without_Using_Cancelled()
    {
        var assignment = Assigned();

        assignment.CompleteBecauseResponseCancelled(Later);

        Assert.Equal(EmergencyAssignmentStatus.Completed, assignment.Status);
        Assert.Equal(Later, assignment.CompletedAtUtc);
    }

    [Fact]
    public void Response_Cancellation_Turns_A_Cancelled_Row_Into_Completed()
    {
        var assignment = EmergencyAssignment.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            AssignedAt,
            EmergencyAssignmentStatus.Cancelled);

        assignment.CompleteBecauseResponseCancelled(Later);

        Assert.Equal(EmergencyAssignmentStatus.Completed, assignment.Status);
        Assert.Equal(Later, assignment.CompletedAtUtc);
    }

    [Fact]
    public void Response_Cancellation_Keeps_An_Existing_Completion_Time()
    {
        var completedAt = AssignedAt.AddMinutes(30);
        var assignment = EmergencyAssignment.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            AssignedAt,
            EmergencyAssignmentStatus.Completed,
            completedAtUtc: completedAt);

        assignment.CompleteBecauseResponseCancelled(Later);

        Assert.Equal(EmergencyAssignmentStatus.Completed, assignment.Status);
        Assert.Equal(completedAt, assignment.CompletedAtUtc);
    }

    [Fact]
    public void Same_Resource_Can_Be_Created_For_Two_Responses()
    {
        var resourceId = Guid.NewGuid();
        var first = EmergencyAssignment.Create(Guid.NewGuid(), resourceId, AssignedAt, EmergencyAssignmentStatus.Assigned);
        var second = EmergencyAssignment.Create(Guid.NewGuid(), resourceId, AssignedAt, EmergencyAssignmentStatus.Assigned);

        Assert.Equal(resourceId, first.EmergencyResourceId);
        Assert.Equal(resourceId, second.EmergencyResourceId);
        Assert.NotEqual(first.EmergencyResponseId, second.EmergencyResponseId);
    }

    private static EmergencyAssignment Assigned() =>
        EmergencyAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), AssignedAt, EmergencyAssignmentStatus.Assigned);
}
