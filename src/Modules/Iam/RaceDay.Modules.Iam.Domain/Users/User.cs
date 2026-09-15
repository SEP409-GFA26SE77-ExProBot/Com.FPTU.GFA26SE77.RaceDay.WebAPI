using RaceDay.Shared.Domain.Abstractions;

namespace RaceDay.Modules.Iam.Domain.Users;

public sealed class User : AggregateRoot
{
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    private User()
    {
    }

    public static User Create(string email, string passwordHash)
    {
        var user = new User
        {
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash
        };

        user.Raise(new UserCreatedDomainEvent(user.Id, user.Email));
        return user;
    }
}

public sealed record UserCreatedDomainEvent(Guid UserId, string Email) : DomainEvent;
