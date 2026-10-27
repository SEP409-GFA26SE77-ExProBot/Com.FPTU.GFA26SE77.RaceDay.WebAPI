namespace RaceDay.Shared.Web.Auth;

public static class AuthPolicies
{
    public const string PublicUser = "PublicUser";
    public const string AdminOnly = "AdminOnly";
    /// <summary>IAM login JWT only — chặn Wear device token (token_use=wear / device_id).</summary>
    public const string HumanUser = "HumanUser";
    /// <summary>IAM role Admin | Organizer | Coordinator + không phải wear token.</summary>
    public const string OrganizerOrCoordinator = "OrganizerOrCoordinator";
    /// <summary>Wear OS JWT: scope=sos:create + token_use=wear.</summary>
    public const string SosWrite = "SosWrite";
}
