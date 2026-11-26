using System.Security.Cryptography;
using System.Text;

namespace RaceDay.Modules.RaceMonitoring.Application.TimingReads;

public static class RfidGatewayAuthenticator
{
    public const string HeaderName = "X-Service-Key";

    public static bool IsValid(string? provided, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(provided))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
