namespace RaceDay.Shared.Web.Auth;

public static class AuthPolicies
{
    public const string PublicUser = "PublicUser";
    public const string AdminOnly = "AdminOnly";
    /// <summary>Platform manager: duyệt giải + catalog gói (không quản trị hệ thống/tài khoản).</summary>
    public const string ManagerOnly = "ManagerOnly";
    /// <summary>Manager hoặc Admin — duyệt giải ngân / vận hành tài chính sự kiện.</summary>
    public const string ManagerOrAdmin = "ManagerOrAdmin";
    /// <summary>IAM login JWT only — chặn Wear device token (token_use=wear / device_id).</summary>
    public const string HumanUser = "HumanUser";
    /// <summary>IAM role Admin | Organizer | Race-staff + không phải wear token.</summary>
    public const string OrganizerOrRaceStaff = "OrganizerOrRaceStaff";
    /// <summary>Wear OS JWT: scope=sos:create + token_use=wear.</summary>
    public const string SosWrite = "SosWrite";
}
