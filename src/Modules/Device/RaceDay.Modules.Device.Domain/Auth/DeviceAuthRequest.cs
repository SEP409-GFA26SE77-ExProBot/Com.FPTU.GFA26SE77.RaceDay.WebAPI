using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Device.Domain.Auth;

public sealed class DeviceAuthRequest : AggregateRoot
{
    public string ClientDeviceId { get; private set; } = string.Empty;
    public string DeviceCodeHash { get; private set; } = string.Empty;
    public string UserCode { get; private set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public Guid? RunnerUserId { get; private set; }
    public int FailedAttempts { get; private set; }
    public bool Consumed { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private DeviceAuthRequest()
    {
    }

    public static DeviceAuthRequest Create(
        string clientDeviceId,
        string deviceCodeHash,
        string userCode,
        DateTime expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(clientDeviceId))
        {
            throw new DomainException("Thiếu deviceId.");
        }

        if (string.IsNullOrWhiteSpace(deviceCodeHash) || string.IsNullOrWhiteSpace(userCode))
        {
            throw new DomainException("Mã thiết bị không hợp lệ.");
        }

        return new DeviceAuthRequest
        {
            Id = Guid.NewGuid(),
            ClientDeviceId = clientDeviceId.Trim(),
            DeviceCodeHash = deviceCodeHash,
            UserCode = NormalizeUserCode(userCode),
            ExpiresAtUtc = EnsureUtc(expiresAtUtc),
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public bool IsExpired(DateTime utcNow) => utcNow >= ExpiresAtUtc;

    public void Approve(Guid runnerUserId)
    {
        if (Consumed)
        {
            throw new DomainException("Mã đã được dùng.");
        }

        if (IsExpired(DateTime.UtcNow))
        {
            throw new DomainException("Mã đã hết hạn.");
        }

        if (runnerUserId == Guid.Empty)
        {
            throw new DomainException("Runner không hợp lệ.");
        }

        RunnerUserId = runnerUserId;
        ApprovedAtUtc = DateTime.UtcNow;
    }

    public void MarkConsumed()
    {
        if (Consumed)
        {
            throw new DomainException("Mã đã được dùng.");
        }

        if (ApprovedAtUtc is null || RunnerUserId is null)
        {
            throw new DomainException("Chưa được xác nhận.");
        }

        Consumed = true;
    }

    public void RegisterFailedAttempt() => FailedAttempts++;

    public static string NormalizeUserCode(string code)
        => code.Replace("-", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
