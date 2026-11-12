using RaceDay.Modules.Event.Application.Registrations;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.UnitTests.Application;

public class RegistrationSlotHoldTests
{
    [Fact]
    public void TryReleaseHeldSlot_Decrements_Distance_And_Clears_Hold()
    {
        var eventId = Guid.NewGuid();
        var distance = EventDistance.Create(eventId, "5K", 5, capacity: 2, price: 100_000);
        distance.IncrementRegistered();
        Assert.Equal(1, distance.RegisteredCount);

        var reg = EventRegistration.Create(
            eventId, distance.Id, Guid.NewGuid(),
            "A", "a@raceday.vn", "0901111222", null, null, null, null, null, null,
            null, null, null, null, null, "[]", null, null, true);
        reg.ReserveSlotHold(DateTime.UtcNow.AddMinutes(15));

        Assert.True(RegistrationSlotHold.TryReleaseHeldSlot(reg, distance));
        Assert.Equal(0, distance.RegisteredCount);
        Assert.False(reg.HasSlotHold);
        Assert.False(RegistrationSlotHold.TryReleaseHeldSlot(reg, distance));
    }

    [Fact]
    public void IncrementRegistered_Throws_When_Full()
    {
        var distance = EventDistance.Create(Guid.NewGuid(), "5K", 5, capacity: 1, price: 0);
        distance.IncrementRegistered();
        Assert.Throws<DomainException>(() => distance.IncrementRegistered());
    }

    [Fact]
    public void DefaultHoldMinutes_Matches_PayOs_Window()
        => Assert.Equal(15, RegistrationSlotHold.DefaultHoldMinutes);
}
