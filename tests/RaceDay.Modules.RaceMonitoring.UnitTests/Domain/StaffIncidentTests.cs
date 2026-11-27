using RaceDay.Modules.RaceMonitoring.Contracts.StaffIncidents;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.StaffMonitoring;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Domain;

public class StaffIncidentTests
{
    private static readonly DateTime At = new(2026, 11, 27, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void LeftZone_IsOpenOutOfZoneRowWithSnapshot()
    {
        var incident = StaffIncident.LeftZone(Author(), new StaffIncidentLocation(10.5, 106.7, 12), At);

        Assert.Equal(StaffIncidentKind.OutOfZone, incident.Kind);
        Assert.Equal("Lan", incident.StaffName);
        Assert.Equal("photographer", incident.Role);
        Assert.Equal("Theo đoàn", incident.ZoneName);
        Assert.Equal(10.5, incident.Latitude);
        Assert.Equal(At, incident.OccurredAtUtc);
        Assert.Null(incident.ReturnedAtUtc);
        Assert.Null(incident.IncidentType);
        Assert.Null(incident.Description);
    }

    [Fact]
    public void Report_TrimsTextAndKeepsOptionalParts()
    {
        var incident = StaffIncident.Report(Author(), ContingencyIncidentType.Logistics, "  Hư xe  ", " ", null, At);

        Assert.Equal(StaffIncidentKind.Report, incident.Kind);
        Assert.Equal(ContingencyIncidentType.Logistics, incident.IncidentType);
        Assert.Equal("Hư xe", incident.Description);
        Assert.Null(incident.PhotoUrl);
        Assert.Null(incident.Latitude);
    }

    [Fact]
    public void Report_RefusesEmptyOrLongDescription()
    {
        Assert.Throws<DomainException>(() =>
            StaffIncident.Report(Author(), ContingencyIncidentType.Other, " ", null, null, At));
        Assert.Throws<DomainException>(() => StaffIncident.Report(Author(), ContingencyIncidentType.Other,
            new string('x', StaffIncidentFieldLengths.Description + 1), null, null, At));
    }

    [Theory]
    [InlineData(91, 0, null)]
    [InlineData(0, 181, null)]
    [InlineData(0, 0, -1.0)]
    [InlineData(double.NaN, 0, null)]
    public void Create_RefusesInvalidLocation(double latitude, double longitude, double? accuracy)
    {
        Assert.Throws<DomainException>(() =>
            StaffIncident.LeftZone(Author(), new StaffIncidentLocation(latitude, longitude, accuracy), At));
    }

    [Fact]
    public void Create_RefusesMissingStaffAndCutsLongZoneList()
    {
        Assert.Throws<DomainException>(() => StaffIncident.Report(
            Author() with { UserId = Guid.Empty }, ContingencyIncidentType.Other, "x", null, null, At));
        Assert.Throws<DomainException>(() => StaffIncident.Report(
            Author() with { Name = " " }, ContingencyIncidentType.Other, "x", null, null, At));

        var incident = StaffIncident.Report(Author() with { ZoneName = new string('z', 800) },
            ContingencyIncidentType.Other, "x", null, null, At);
        Assert.Equal(StaffIncidentFieldLengths.ZoneName, incident.ZoneName!.Length);
    }

    [Fact]
    public void ContractTypes_MatchTheStoredEnum()
    {
        Assert.Equal(Enum.GetNames<ContingencyIncidentType>(), StaffIncidentTypes.All);
        Assert.Equal(StaffIncidentKind.OutOfZone.ToString(), StaffIncidentKinds.OutOfZone);
        Assert.Equal(StaffIncidentKind.Report.ToString(), StaffIncidentKinds.Report);
    }

    private static StaffIncidentAuthor Author()
        => new(Guid.NewGuid(), Guid.NewGuid(), "Lan", "photographer", "Theo đoàn");
}
