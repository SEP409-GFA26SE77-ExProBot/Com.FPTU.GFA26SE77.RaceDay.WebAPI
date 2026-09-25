namespace RaceDay.Shared.Application.Abstractions;

public interface ICurrentUserAccessor
{
    Guid? UserId { get; }
}
