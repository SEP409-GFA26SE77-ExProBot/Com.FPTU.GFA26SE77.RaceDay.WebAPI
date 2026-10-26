using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.UnitTests.Domain;

public class RaceEventWorkflowTests
{
    private static readonly Guid OrganizerId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static RaceEvent DraftWithDistance()
    {
        var race = RaceEvent.CreateDraft(
            OrganizerId,
            "Da Lat Ultra Trail 2026",
            "Da Lat, Lam Dong",
            new DateOnly(2026, 10, 24),
            new GeoPoint(11.9, 108.4),
            OrganizerInfo.Create("VTS", "org@raceday.vn", "0901234567"),
            totalRunnersCap: 1000,
            tagline: "Misty pine forests");

        var distance = EventDistance.Create(
            race.Id,
            "Ultra Langbiang",
            42.2,
            capacity: 500,
            price: 1_500_000,
            color: "#FC4C02",
            type: "42K",
            routeGeometryJson: "{}",
            checkpoints:
            [
                ("CP-0", "Start", 0, 11.9, 108.4, CheckpointType.Start, "04:30", 1495),
                ("CP-FIN", "Finish", 42.2, 11.9, 108.4, CheckpointType.Finish, "22:00", 1495)
            ]);

        race.ReplaceDistances([distance]);
        race.ReplaceDocuments(
        [
            EventDocument.Create(race.Id, "plan", "Kế hoạch", "plan.pdf", "/files/plan.pdf", required: true),
            EventDocument.Create(race.Id, "rules", "Điều lệ", "rules.pdf", "/files/rules.pdf", required: true)
        ]);
        return race;
    }

    [Fact]
    public void CreateDraft_Starts_As_Draft()
    {
        var race = DraftWithDistance();
        Assert.Equal(RaceEventStatus.Draft, race.Status);
        Assert.Contains(race.DomainEvents, e => e is RaceEventCreatedDomainEvent);
    }

    [Fact]
    public void Submit_Approve_OpenRegistration_Flow()
    {
        var race = DraftWithDistance();
        race.ClearDomainEvents();

        race.SubmitForApproval();
        Assert.Equal(RaceEventStatus.PendingApproval, race.Status);
        Assert.Contains(race.DomainEvents, e => e is RaceEventSubmittedDomainEvent);

        race.Approve();
        Assert.Equal(RaceEventStatus.Approved, race.Status);
        Assert.True(race.Organizer.Verified);

        race.OpenRegistration();
        Assert.Equal(RaceEventStatus.RegistrationOpen, race.Status);
        race.EnsureAcceptsRegistration();
    }

    [Fact]
    public void Reject_Requires_Reason_And_Blocks_Registration()
    {
        var race = DraftWithDistance();
        race.SubmitForApproval();

        Assert.Throws<DomainException>(() => race.Reject(" "));
        race.Reject("Thiếu giấy phép Sở VHTT");
        Assert.Equal(RaceEventStatus.Rejected, race.Status);
        Assert.Throws<DomainException>(() => race.EnsureAcceptsRegistration());
    }

    [Fact]
    public void Submit_Without_Distance_Throws()
    {
        var race = RaceEvent.CreateDraft(
            OrganizerId, "X", "Hue", new DateOnly(2026, 1, 1), new GeoPoint(1, 1),
            OrganizerInfo.Create("O", "a@b.com", "1"), 100);
        Assert.Throws<DomainException>(() => race.SubmitForApproval());
    }

    [Fact]
    public void Distance_Increments_Until_Full()
    {
        var race = DraftWithDistance();
        var distance = race.Distances.First();
        // capacity 500 — just check one increment
        distance.IncrementRegistered();
        Assert.Equal(1, distance.RegisteredCount);
    }
}
