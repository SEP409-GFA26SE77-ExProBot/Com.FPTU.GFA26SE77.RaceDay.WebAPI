using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Shared.UnitTests;

public class DomainExceptionTests
{
    [Fact]
    public void DomainException_Keeps_Message()
    {
        var ex = new DomainException("invalid");
        Assert.Equal("invalid", ex.Message);
    }
}
