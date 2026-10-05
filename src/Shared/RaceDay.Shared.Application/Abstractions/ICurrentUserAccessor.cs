namespace RaceDay.Shared.Application.Abstractions;

public interface ICurrentUserAccessor
{
    Guid? UserId { get; }

    bool IsInRole(string role);

    bool IsAdmin => IsInRole("Admin");
}
