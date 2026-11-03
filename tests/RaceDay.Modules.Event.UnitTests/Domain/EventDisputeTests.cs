using RaceDay.Modules.Event.Domain.Disputes;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.UnitTests.Domain;

public class EventDisputeTests
{
    [Theory]
    [InlineData("", "Description")]
    [InlineData(" ", "Description")]
    [InlineData("Title", "")]
    [InlineData("Title", " ")]
    public void Create_BlankContent_Throws(string title, string description)
        => Assert.Throws<DomainException>(() => EventDispute.Create(
            Guid.NewGuid(), Guid.NewGuid(), title, description, DateTime.UtcNow));

    [Fact]
    public void Create_InvalidReferencesOrOversizedContent_Throws()
    {
        var eventId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        Assert.Throws<DomainException>(() => EventDispute.Create(Guid.Empty, userId, "Title", "Body", now));
        Assert.Throws<DomainException>(() => EventDispute.Create(eventId, Guid.Empty, "Title", "Body", now));
        Assert.Throws<DomainException>(() => EventDispute.Create(eventId, userId, new('x', 201), "Body", now));
        Assert.Throws<DomainException>(() => EventDispute.Create(eventId, userId, "Title", new('x', 4001), now));
    }
}
