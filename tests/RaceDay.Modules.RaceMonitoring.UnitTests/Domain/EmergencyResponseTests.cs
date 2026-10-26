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

    private static EmergencyResponse Create(Guid sourceSosAlertId) =>
        EmergencyResponse.Create(
            sourceSosAlertId,
            EmergencyIncidentType.Other,
            EmergencyPriority.Medium,
            EmergencyResponseStatus.Open);
}
