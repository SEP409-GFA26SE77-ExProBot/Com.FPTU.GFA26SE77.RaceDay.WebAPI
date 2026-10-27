using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Domain;

public class EmergencyResponseTests
{
    [Fact]
    public void Create_Rejects_Empty_Source_Sos()
    {
        var ex = Assert.Throws<DomainException>(() => Create(Guid.Empty));
        Assert.Equal("Tham chiếu SOS không hợp lệ.", ex.Message);
    }

    [Fact]
    public void Create_Stores_Source_Sos_And_Response_State()
    {
        var sourceSosAlertId = Guid.NewGuid();
        var assignedAt = new DateTime(2026, 10, 26, 8, 0, 0, DateTimeKind.Utc);
        var resolvedAt = new DateTime(2026, 10, 26, 9, 30, 0, DateTimeKind.Utc);

        var response = EmergencyResponse.Create(
            sourceSosAlertId,
            EmergencyIncidentType.Medical,
            EmergencyPriority.High,
            EmergencyResponseStatus.Resolved,
            assignedAt,
            resolvedAt,
            "  Đã điều xe cứu thương.  ");

        Assert.Equal(sourceSosAlertId, response.SourceSosAlertId);
        Assert.Equal(EmergencyIncidentType.Medical, response.IncidentType);
        Assert.Equal(EmergencyPriority.High, response.Priority);
        Assert.Equal(EmergencyResponseStatus.Resolved, response.Status);
        Assert.Equal(assignedAt, response.AssignedAtUtc);
        Assert.Equal(resolvedAt, response.ResolvedAtUtc);
        Assert.Equal("Đã điều xe cứu thương.", response.ResolutionNotes);
    }

    [Fact]
    public void Create_Allows_Open_Response_Without_Resolution()
    {
        var response = Create(Guid.NewGuid());

        Assert.Equal(EmergencyResponseStatus.Open, response.Status);
        Assert.Null(response.AssignedAtUtc);
        Assert.Null(response.ResolvedAtUtc);
        Assert.Null(response.ResolutionNotes);
    }

    [Fact]
    public void Acknowledge_From_Open_Sets_Assigned_And_Timestamp()
    {
        var sourceSosAlertId = Guid.NewGuid();
        var response = Create(sourceSosAlertId);
        var assignedAt = new DateTime(2026, 10, 27, 3, 0, 0, DateTimeKind.Utc);

        response.Acknowledge(assignedAt);

        Assert.Equal(sourceSosAlertId, response.SourceSosAlertId);
        Assert.Equal(EmergencyResponseStatus.Assigned, response.Status);
        Assert.Equal(assignedAt, response.AssignedAtUtc);
        Assert.Null(response.ResolvedAtUtc);
        Assert.Null(response.ResolutionNotes);
    }

    [Theory]
    [InlineData(EmergencyResponseStatus.Assigned)]
    [InlineData(EmergencyResponseStatus.InProgress)]
    [InlineData(EmergencyResponseStatus.Resolved)]
    [InlineData(EmergencyResponseStatus.Cancelled)]
    public void Acknowledge_Rejects_Status(EmergencyResponseStatus status)
    {
        var response = CreateWithStatus(status);

        var ex = Assert.Throws<DomainException>(() => response.Acknowledge(Utc(4)));

        Assert.Equal("Chỉ phản hồi đang mở mới được xác nhận.", ex.Message);
        Assert.Equal(status, response.Status);
    }

    [Fact]
    public void UpdateProgress_From_Assigned_Sets_Priority_And_InProgress()
    {
        var response = CreateWithStatus(EmergencyResponseStatus.Assigned);
        var assignedAt = response.AssignedAtUtc;

        response.UpdateProgress(EmergencyPriority.Critical, EmergencyResponseStatus.InProgress);

        Assert.Equal(EmergencyPriority.Critical, response.Priority);
        Assert.Equal(EmergencyResponseStatus.InProgress, response.Status);
        Assert.Equal(assignedAt, response.AssignedAtUtc);
        Assert.Null(response.ResolvedAtUtc);
    }

    [Fact]
    public void UpdateProgress_From_InProgress_Updates_Priority_Only()
    {
        var response = CreateWithStatus(EmergencyResponseStatus.InProgress);

        response.UpdateProgress(EmergencyPriority.Low, null);

        Assert.Equal(EmergencyPriority.Low, response.Priority);
        Assert.Equal(EmergencyResponseStatus.InProgress, response.Status);
    }

    [Theory]
    [InlineData(EmergencyResponseStatus.Open)]
    [InlineData(EmergencyResponseStatus.Resolved)]
    [InlineData(EmergencyResponseStatus.Cancelled)]
    public void UpdateProgress_Rejects_Inactive_Status(EmergencyResponseStatus status)
    {
        var response = CreateWithStatus(status);

        var ex = Assert.Throws<DomainException>(() => response.UpdateProgress(EmergencyPriority.High, null));

        Assert.Equal("Không thể cập nhật tiến độ ở trạng thái hiện tại.", ex.Message);
        Assert.Equal(status, response.Status);
    }

    [Fact]
    public void UpdateProgress_Rejects_Status_Other_Than_InProgress()
    {
        var response = CreateWithStatus(EmergencyResponseStatus.Assigned);

        var ex = Assert.Throws<DomainException>(() =>
            response.UpdateProgress(null, EmergencyResponseStatus.Resolved));

        Assert.Equal("Chỉ được chuyển tiến độ sang InProgress.", ex.Message);
        Assert.Equal(EmergencyResponseStatus.Assigned, response.Status);
    }

    [Fact]
    public void UpdateProgress_Rejects_InProgress_When_Already_InProgress()
    {
        var response = CreateWithStatus(EmergencyResponseStatus.InProgress);

        var ex = Assert.Throws<DomainException>(() =>
            response.UpdateProgress(null, EmergencyResponseStatus.InProgress));

        Assert.Equal("Không thể chuyển tiến độ ở trạng thái hiện tại.", ex.Message);
    }

    [Fact]
    public void Resolve_From_Assigned_Sets_Resolution_Metadata()
    {
        var sourceSosAlertId = Guid.NewGuid();
        var response = EmergencyResponse.Create(
            sourceSosAlertId,
            EmergencyIncidentType.Heat,
            EmergencyPriority.High,
            EmergencyResponseStatus.Assigned,
            Utc(1));
        var resolvedAt = Utc(5);

        response.Resolve("  Đã chuyển viện.  ", resolvedAt);

        Assert.Equal(sourceSosAlertId, response.SourceSosAlertId);
        Assert.Equal(EmergencyResponseStatus.Resolved, response.Status);
        Assert.Equal(resolvedAt, response.ResolvedAtUtc);
        Assert.Equal("Đã chuyển viện.", response.ResolutionNotes);
        Assert.Equal(Utc(1), response.AssignedAtUtc);
    }

    [Fact]
    public void Resolve_From_InProgress_Sets_Resolution_Metadata()
    {
        var response = CreateWithStatus(EmergencyResponseStatus.InProgress);

        response.Resolve("Ổn định tại chỗ.", Utc(6));

        Assert.Equal(EmergencyResponseStatus.Resolved, response.Status);
        Assert.Equal(Utc(6), response.ResolvedAtUtc);
        Assert.Equal("Ổn định tại chỗ.", response.ResolutionNotes);
    }

    [Theory]
    [InlineData(EmergencyResponseStatus.Open)]
    [InlineData(EmergencyResponseStatus.Resolved)]
    [InlineData(EmergencyResponseStatus.Cancelled)]
    public void Resolve_Rejects_Status(EmergencyResponseStatus status)
    {
        var response = CreateWithStatus(status);

        var ex = Assert.Throws<DomainException>(() => response.Resolve("Đã xong.", Utc(7)));

        Assert.Equal("Phản hồi khẩn cấp không thể kết thúc ở trạng thái hiện tại.", ex.Message);
        Assert.Equal(status, response.Status);
    }

    [Fact]
    public void Resolve_Requires_Notes()
    {
        var response = CreateWithStatus(EmergencyResponseStatus.Assigned);

        var ex = Assert.Throws<DomainException>(() => response.Resolve("   ", Utc(8)));

        Assert.Equal("Nội dung kết thúc phản hồi không được để trống.", ex.Message);
        Assert.Equal(EmergencyResponseStatus.Assigned, response.Status);
        Assert.Null(response.ResolvedAtUtc);
    }

    [Theory]
    [InlineData(EmergencyResponseStatus.Open)]
    [InlineData(EmergencyResponseStatus.Assigned)]
    [InlineData(EmergencyResponseStatus.InProgress)]
    public void Cancel_Records_Reason_And_Time_Without_Touching_Source_Sos(EmergencyResponseStatus status)
    {
        var sourceSosAlertId = Guid.NewGuid();
        var response = EmergencyResponse.Create(
            sourceSosAlertId,
            EmergencyIncidentType.Medical,
            EmergencyPriority.High,
            status,
            status == EmergencyResponseStatus.Open ? null : Utc(1));
        var cancelledAt = Utc(9);

        response.Cancel("  Runner đã ổn định.  ", cancelledAt);

        Assert.Equal(sourceSosAlertId, response.SourceSosAlertId);
        Assert.Equal(EmergencyResponseStatus.Cancelled, response.Status);
        Assert.Equal(cancelledAt, response.CancelledAtUtc);
        Assert.Equal("Runner đã ổn định.", response.CancellationReason);
        Assert.Null(response.ResolvedAtUtc);
        Assert.Null(response.ResolutionNotes);
    }

    [Theory]
    [InlineData(EmergencyResponseStatus.Resolved)]
    [InlineData(EmergencyResponseStatus.Cancelled)]
    public void Cancel_Rejects_Terminal_Status(EmergencyResponseStatus status)
    {
        var response = CreateWithStatus(status);

        var ex = Assert.Throws<DomainException>(() => response.Cancel("Không còn việc.", Utc(9)));

        Assert.Equal("Phản hồi khẩn cấp không thể hủy ở trạng thái hiện tại.", ex.Message);
        Assert.Equal(status, response.Status);
        Assert.Null(response.CancellationReason);
    }

    [Fact]
    public void Cancel_Requires_Reason()
    {
        var response = Create(Guid.NewGuid());

        var ex = Assert.Throws<DomainException>(() => response.Cancel("   ", Utc(9)));

        Assert.Equal("Lý do hủy phản hồi không được để trống.", ex.Message);
        Assert.Equal(EmergencyResponseStatus.Open, response.Status);
        Assert.Null(response.CancelledAtUtc);
    }

    private static EmergencyResponse Create(Guid sourceSosAlertId) =>
        EmergencyResponse.Create(
            sourceSosAlertId,
            EmergencyIncidentType.Other,
            EmergencyPriority.Medium,
            EmergencyResponseStatus.Open);

    private static EmergencyResponse CreateWithStatus(EmergencyResponseStatus status)
    {
        var active = status is EmergencyResponseStatus.Assigned
            or EmergencyResponseStatus.InProgress
            or EmergencyResponseStatus.Resolved;
        return EmergencyResponse.Create(
            Guid.NewGuid(),
            EmergencyIncidentType.Medical,
            EmergencyPriority.Medium,
            status,
            active ? Utc(1) : null,
            status == EmergencyResponseStatus.Resolved ? Utc(2) : null,
            status == EmergencyResponseStatus.Resolved ? "Đã xử lý." : null);
    }

    private static DateTime Utc(int hour) => new(2026, 10, 27, hour, 0, 0, DateTimeKind.Utc);
}
