using NSubstitute;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Application.Supplies;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Supplies;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.UnitTests.Application;

public class EventSuppliesHandlerTests
{
    private static readonly Guid OrganizerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static RaceEvent DemoRace()
    {
        var race = RaceEvent.CreateDraft(
            OrganizerId,
            "Demo Run",
            "Quy Nhon",
            new DateOnly(2026, 11, 14),
            new GeoPoint(13.7, 109.2),
            OrganizerInfo.Create("Org", "org@raceday.vn", "0900000000"),
            500);
        // EnsureOrganizer compares OrganizerUserId; event Id only used for repo lookup key.
        return race;
    }

    [Fact]
    public async Task ReplaceSupplies_Replaces_All_And_Accepts_NonGuid_Client_Ids()
    {
        var events = Substitute.For<IRaceEventRepository>();
        var supplies = Substitute.For<IEventSupplyItemRepository>();
        var uow = Substitute.For<IEventUnitOfWork>();
        var current = Substitute.For<ICurrentUserAccessor>();
        var race = DemoRace();
        events.GetByIdAsync(race.Id, Arg.Any<CancellationToken>()).Returns(race);
        current.UserId.Returns(OrganizerId);

        var body = new UpdateEventSuppliesRequest(
        [
            new EventSupplyItemUpsertDto("sup-1", "Nước khoáng thiên nhiên 500ml", 80, "thùng",
                "Thùng 24 chai. Phân bổ đồng đều cho các trạm tiếp nước."),
            new EventSupplyItemUpsertDto(null, "Gel năng lượng chạy bộ thể thao", 12, "thùng", null),
        ]);

        var result = await new ReplaceEventSuppliesCommandHandler(events, supplies, uow, current)
            .Handle(new ReplaceEventSuppliesCommand(race.Id, body), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.All(result, x => Assert.True(Guid.TryParse(x.Id, out _)));
        Assert.Equal(80, result[0].Quantity);
        await supplies.Received(1).RemoveAllForEventAsync(race.Id, Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListSupplies_Rejects_Non_Organizer()
    {
        var race = DemoRace();
        var events = Substitute.For<IRaceEventRepository>();
        var supplies = Substitute.For<IEventSupplyItemRepository>();
        var current = Substitute.For<ICurrentUserAccessor>();
        events.GetByIdAsync(race.Id, Arg.Any<CancellationToken>()).Returns(race);
        current.UserId.Returns(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        current.IsAdmin.Returns(false);

        await Assert.ThrowsAsync<DomainException>(() =>
            new ListEventSuppliesQueryHandler(events, supplies, current)
                .Handle(new ListEventSuppliesQuery(race.Id), CancellationToken.None));
    }

    [Fact]
    public void EventSupplyItem_Rejects_Negative_Quantity()
    {
        Assert.Throws<DomainException>(() =>
            EventSupplyItem.Create(Guid.NewGuid(), "X", -1));
    }
}
