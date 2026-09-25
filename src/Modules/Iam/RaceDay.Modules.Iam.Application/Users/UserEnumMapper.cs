using DomainGender = RaceDay.Modules.Iam.Domain.Users.Gender;
using DomainUserStatus = RaceDay.Modules.Iam.Domain.Users.UserStatus;

namespace RaceDay.Modules.Iam.Application.Users;

/// <summary>Wire/DTO enums — mirror Domain values; keep Api free of Domain types.</summary>
public enum GenderDto : short
{
    Unknown = 0,
    Male = 1,
    Female = 2
}

public enum UserStatusDto : short
{
    Locked = 0,
    Active = 1,
    Pending = 2
}

internal static class UserEnumMapper
{
    public static DomainGender ToDomain(this GenderDto value) => (DomainGender)(short)value;

    public static DomainUserStatus ToDomain(this UserStatusDto value) => (DomainUserStatus)(short)value;

    public static GenderDto ToDto(this DomainGender value) => (GenderDto)(short)value;

    public static UserStatusDto ToDto(this DomainUserStatus value) => (UserStatusDto)(short)value;
}
