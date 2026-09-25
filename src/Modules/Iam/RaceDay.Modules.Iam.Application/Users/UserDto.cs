using RaceDay.Modules.Iam.Domain.Users;

namespace RaceDay.Modules.Iam.Application.Users;

public sealed record UserDto(
    Guid Id,
    string Email,
    string FullName,
    string? Phone,
    DateOnly? DateOfBirth,
    GenderDto Gender,
    UserStatusDto Status,
    bool EmailVerified,
    Guid RoleId,
    string? RoleName,
    string? ProfileDescription,
    DateTime CreatedAt,
    DateTime UpdatedAt);

internal static class UserDtoMapper
{
    public static UserDto ToDto(User user, string? roleName) => new(
        user.Id,
        user.Email,
        user.FullName,
        user.Phone,
        user.DateOfBirth,
        user.Gender.ToDto(),
        user.Status.ToDto(),
        user.EmailVerified,
        user.RoleId,
        roleName,
        user.Profile?.Description,
        user.CreatedAt,
        user.UpdatedAt);
}
