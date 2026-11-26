using Microsoft.Extensions.Configuration;

namespace RaceDay.Modules.Iam.Application.Users.ProvisionStaff;

/// <summary>Mật khẩu gốc staff (form tạo / khôi phục). Ưu tiên config, fallback seed docs.</summary>
public static class StaffCredentialDefaults
{
    public const string FallbackPassword = "MatKhau@123";

    public static string ResolveDefaultPassword(IConfiguration configuration)
    {
        var configured = configuration["Iam:StaffDefaultPassword"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        var seed = configuration["Iam:SeedPassword"];
        if (!string.IsNullOrWhiteSpace(seed))
        {
            return seed.Trim();
        }

        return FallbackPassword;
    }
}
