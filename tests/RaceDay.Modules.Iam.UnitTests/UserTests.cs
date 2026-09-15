using RaceDay.Modules.Iam.Domain.Users;

namespace RaceDay.Modules.Iam.UnitTests;

public class UserTests
{
    [Fact]
    public void Create_Raises_UserCreatedDomainEvent()
    {
        var user = User.Create("Dev@RaceDay.local", "hash:demo");

        Assert.Equal("dev@raceday.local", user.Email);
        Assert.Contains(user.DomainEvents, e => e is UserCreatedDomainEvent);
    }
}
