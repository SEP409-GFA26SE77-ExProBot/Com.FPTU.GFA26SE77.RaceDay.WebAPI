using RaceDay.Shared.Domain.Abstractions;

namespace RaceDay.Modules.Iam.Domain.Roles;

public sealed class Role : AggregateRoot
{
    public string RoleName { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    private Role()
    {
    }

    public static Role Create(string roleName, string? description = null)
    {
        return new Role
        {
            RoleName = roleName.Trim(),
            Description = description
        };
    }

    public static Role Restore(Guid id, string roleName, string? description)
    {
        return new Role
        {
            Id = id,
            RoleName = roleName,
            Description = description
        };
    }
}
