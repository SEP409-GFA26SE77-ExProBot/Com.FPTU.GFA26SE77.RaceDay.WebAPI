using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Device.Domain.Auth;

public sealed class LinkedDevice : AggregateRoot
{
    public Guid RunnerUserId { get; private set; }
    public string ClientDeviceId { get; private set; } = string.Empty;
    public string Name { get; private set; } = "Wear OS watch";
    public string? RefreshTokenHash { get; private set; }
    public DateTime LinkedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }

    private LinkedDevice()
    {
    }

    public bool IsRevoked => RevokedAtUtc is not null;

    public static LinkedDevice Create(
        Guid runnerUserId,
        string clientDeviceId,
        string? name,
        string? refreshTokenHash)
    {
        if (runnerUserId == Guid.Empty)
        {
            throw new DomainException("Runner không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(clientDeviceId))
        {
            throw new DomainException("Thiếu deviceId.");
        }

        return new LinkedDevice
        {
            Id = Guid.NewGuid(),
            RunnerUserId = runnerUserId,
            ClientDeviceId = clientDeviceId.Trim(),
            Name = string.IsNullOrWhiteSpace(name) ? "Wear OS watch" : name.Trim(),
            RefreshTokenHash = refreshTokenHash,
            LinkedAtUtc = DateTime.UtcNow
        };
    }

    public void RotateRefreshToken(string refreshTokenHash)
    {
        EnsureActive();
        RefreshTokenHash = refreshTokenHash;
    }

    public void Revoke()
    {
        if (IsRevoked)
        {
            return;
        }

        RevokedAtUtc = DateTime.UtcNow;
        RefreshTokenHash = null;
    }

    public void EnsureActive()
    {
        if (IsRevoked)
        {
            throw new DomainException("Thiết bị đã bị thu hồi.");
        }
    }
}
