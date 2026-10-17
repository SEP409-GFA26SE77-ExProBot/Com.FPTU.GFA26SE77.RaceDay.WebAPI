using System.Security.Cryptography;
using System.Text;

namespace RaceDay.Modules.Device.Application.Auth;

public static class DeviceCodeCrypto
{
    private const string UserCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string GenerateDeviceCode()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string plaintext)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string GenerateUserCode()
    {
        Span<char> chars = stackalloc char[8];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = UserCodeAlphabet[RandomNumberGenerator.GetInt32(UserCodeAlphabet.Length)];
        }

        return new string(chars);
    }

    public static string GenerateRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
}
