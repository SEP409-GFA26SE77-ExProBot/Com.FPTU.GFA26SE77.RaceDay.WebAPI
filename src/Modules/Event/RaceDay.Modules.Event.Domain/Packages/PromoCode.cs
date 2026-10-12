using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Domain.Packages;

public enum PromoCodeStatus
{
    Active = 0,
    Inactive = 1
}

public sealed class PromoCode : AggregateRoot
{
    public string Code { get; private set; } = string.Empty;
    public decimal PercentOff { get; private set; }
    public PromoCodeStatus Status { get; private set; } = PromoCodeStatus.Active;
    public DateTime? ExpiresAtUtc { get; private set; }
    public int? MaxRedemptions { get; private set; }
    public int UsedCount { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private PromoCode() { }

    public static PromoCode Create(
        string code,
        decimal percentOff,
        DateTime? expiresAtUtc = null,
        int? maxRedemptions = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("Mã khuyến mãi không được để trống.");

        if (percentOff is <= 0 or > 1)
            throw new DomainException("PercentOff phải trong khoảng (0, 1].");

        if (maxRedemptions is <= 0)
            throw new DomainException("MaxRedemptions phải > 0 hoặc null (không giới hạn).");

        var now = DateTime.UtcNow;
        return new PromoCode
        {
            Code = code.Trim().ToUpperInvariant(),
            PercentOff = percentOff,
            Status = PromoCodeStatus.Active,
            ExpiresAtUtc = expiresAtUtc,
            MaxRedemptions = maxRedemptions,
            UsedCount = 0,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    public void Activate()
    {
        Status = PromoCodeStatus.Active;
        Touch();
    }

    public void Deactivate()
    {
        Status = PromoCodeStatus.Inactive;
        Touch();
    }

    public void UpdateExpiry(DateTime? expiresAtUtc)
    {
        ExpiresAtUtc = expiresAtUtc;
        Touch();
    }

    public void UpdateMaxRedemptions(int? maxRedemptions)
    {
        if (maxRedemptions is <= 0)
            throw new DomainException("MaxRedemptions phải > 0 hoặc null.");
        MaxRedemptions = maxRedemptions;
        Touch();
    }

    public void EnsureRedeemable(DateTime now)
    {
        if (Status != PromoCodeStatus.Active)
            throw new DomainException($"Mã '{Code}' đã bị vô hiệu hóa.");

        if (ExpiresAtUtc.HasValue && now >= ExpiresAtUtc.Value)
            throw new DomainException($"Mã '{Code}' đã hết hạn.");

        if (MaxRedemptions.HasValue && UsedCount >= MaxRedemptions.Value)
            throw new DomainException($"Mã '{Code}' đã hết lượt sử dụng.");
    }

    public void RecordRedemption()
    {
        UsedCount++;
        Touch();
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
