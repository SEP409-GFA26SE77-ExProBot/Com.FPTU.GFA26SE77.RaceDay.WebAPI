using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Domain;

public class EmergencyResourceTests
{
    private static readonly Guid EventId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public void Create_Trims_Text_And_Keeps_The_Given_Status()
    {
        var resource = EmergencyResource.Create(
            EventId,
            "  MED-1  ",
            EmergencyResourceType.Ambulance,
            "  Xe cứu thương  ",
            EmergencyResourceStatus.Offline,
            "  Ca đêm  ");

        Assert.Equal(EventId, resource.EventId);
        Assert.Equal("MED-1", resource.ResourceCode);
        Assert.Equal(EmergencyResourceType.Ambulance, resource.ResourceType);
        Assert.Equal("Xe cứu thương", resource.Name);
        Assert.Equal(EmergencyResourceStatus.Offline, resource.Status);
        Assert.Equal("Ca đêm", resource.Notes);
    }

    [Fact]
    public void Create_Rejects_Blank_Or_Too_Long_Code()
    {
        var blank = Assert.Throws<DomainException>(() =>
            EmergencyResource.Create(EventId, "   ", EmergencyResourceType.Other, "Tên", EmergencyResourceStatus.Available));
        var tooLong = Assert.Throws<DomainException>(() =>
            EmergencyResource.Create(
                EventId,
                new string('A', CoordinatorFieldLengths.Code + 1),
                EmergencyResourceType.Other,
                "Tên",
                EmergencyResourceStatus.Available));

        Assert.Equal("Mã nguồn lực không được để trống.", blank.Message);
        Assert.Equal("Mã nguồn lực tối đa 50 ký tự.", tooLong.Message);
    }

    [Fact]
    public void UpdateDetails_Changes_Description_Only()
    {
        var resource = EmergencyResource.Create(
            EventId,
            "MED-1",
            EmergencyResourceType.MedicalTeam,
            "Đội y tế",
            EmergencyResourceStatus.Available,
            "Cũ");
        var id = resource.Id;

        resource.UpdateDetails(EmergencyResourceType.FirstAid, "  Đội sơ cứu  ", "   ");

        Assert.Equal(id, resource.Id);
        Assert.Equal(EventId, resource.EventId);
        Assert.Equal("MED-1", resource.ResourceCode);
        Assert.Equal(EmergencyResourceStatus.Available, resource.Status);
        Assert.Equal(EmergencyResourceType.FirstAid, resource.ResourceType);
        Assert.Equal("Đội sơ cứu", resource.Name);
        Assert.Null(resource.Notes);
    }

    [Fact]
    public void UpdateDetails_Rejects_Blank_Name_And_Undefined_Type()
    {
        var resource = EmergencyResource.Create(
            EventId, "MED-1", EmergencyResourceType.MedicalTeam, "Đội y tế", EmergencyResourceStatus.Assigned);

        var blank = Assert.Throws<DomainException>(() =>
            resource.UpdateDetails(EmergencyResourceType.Ambulance, "  ", "Giữ"));
        var undefined = Assert.Throws<DomainException>(() =>
            resource.UpdateDetails((EmergencyResourceType)99, "Tên mới", null));

        Assert.Equal("Tên nguồn lực không được để trống.", blank.Message);
        Assert.Equal("Loại nguồn lực không hợp lệ.", undefined.Message);
        Assert.Equal("Đội y tế", resource.Name);
        Assert.Equal(EmergencyResourceType.MedicalTeam, resource.ResourceType);
        Assert.Equal(EmergencyResourceStatus.Assigned, resource.Status);
    }
}
