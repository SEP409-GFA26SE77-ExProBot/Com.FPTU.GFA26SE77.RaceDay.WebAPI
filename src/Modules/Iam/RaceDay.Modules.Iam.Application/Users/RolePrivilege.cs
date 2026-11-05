namespace RaceDay.Modules.Iam.Application.Users;

/// <summary>
/// Bậc quyền để chặn lock/unlock ngang cấp hoặc cao hơn.
/// Admin chỉ can thiệp role thấp hơn (không khóa Admin khác / chính mình).
/// </summary>
public static class RolePrivilege
{
    public static int Rank(string? roleName)
    {
        var r = (roleName ?? string.Empty).Trim().ToLowerInvariant();
        return r switch
        {
            "admin" or "administrator" => 100,
            "manager" => 80,
            "organizer" => 60,
            "race-staff" or "racestaff" or "race_staff"
                or "coordinator" or "volunteer" => 50,
            "photographer" => 40,
            "user" or "runner" => 10,
            _ => 0
        };
    }

    public static bool CanManage(string? actorRoleName, string? targetRoleName)
        => Rank(actorRoleName) > Rank(targetRoleName);
}
