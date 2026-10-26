using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Domain.Packages;

public enum PackageOptionStatus
{
    Active = 0,
    Inactive = 1
}

/// <summary>Admin-configured duration tier for event package checkout.</summary>
public sealed class PackageDurationOption : AggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public int Months { get; private set; }
    public decimal Price { get; private set; }
    public PackageOptionStatus Status { get; private set; } = PackageOptionStatus.Active;
    public int SortOrder { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private PackageDurationOption() { }

    public static PackageDurationOption Create(string name, int months, decimal price, int sortOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Tên thời hạn gói không được để trống.");
        if (months < 1)
            throw new DomainException("Số tháng phải ≥ 1.");
        if (price < 0)
            throw new DomainException("Giá gói không hợp lệ.");

        var now = DateTime.UtcNow;
        return new PackageDurationOption
        {
            Name = name.Trim(),
            Months = months,
            Price = price,
            Status = PackageOptionStatus.Active,
            SortOrder = sortOrder,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    public void Update(string? name, decimal? price, int? sortOrder)
    {
        if (name is not null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new DomainException("Tên thời hạn gói không được để trống.");
            Name = name.Trim();
        }
        if (price.HasValue)
        {
            if (price.Value < 0)
                throw new DomainException("Giá gói không hợp lệ.");
            Price = price.Value;
        }
        if (sortOrder.HasValue) SortOrder = sortOrder.Value;
        Touch();
    }

    public void Activate() { Status = PackageOptionStatus.Active; Touch(); }
    public void Deactivate() { Status = PackageOptionStatus.Inactive; Touch(); }
    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}

/// <summary>Admin-configured athlete-capacity tier for event package checkout.</summary>
public sealed class PackageCapacityOption : AggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public int MaxParticipants { get; private set; }
    public decimal AddOnFee { get; private set; }
    public PackageOptionStatus Status { get; private set; } = PackageOptionStatus.Active;
    public int SortOrder { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private PackageCapacityOption() { }

    public static PackageCapacityOption Create(string name, int maxParticipants, decimal addOnFee, int sortOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Tên quy mô VĐV không được để trống.");
        if (maxParticipants < 1)
            throw new DomainException("Số VĐV phải ≥ 1.");
        if (addOnFee < 0)
            throw new DomainException("Phụ phí quy mô không hợp lệ.");

        var now = DateTime.UtcNow;
        return new PackageCapacityOption
        {
            Name = name.Trim(),
            MaxParticipants = maxParticipants,
            AddOnFee = addOnFee,
            Status = PackageOptionStatus.Active,
            SortOrder = sortOrder,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    public void Update(string? name, decimal? addOnFee, int? sortOrder)
    {
        if (name is not null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new DomainException("Tên quy mô VĐV không được để trống.");
            Name = name.Trim();
        }
        if (addOnFee.HasValue)
        {
            if (addOnFee.Value < 0)
                throw new DomainException("Phụ phí quy mô không hợp lệ.");
            AddOnFee = addOnFee.Value;
        }
        if (sortOrder.HasValue) SortOrder = sortOrder.Value;
        Touch();
    }

    public void Activate() { Status = PackageOptionStatus.Active; Touch(); }
    public void Deactivate() { Status = PackageOptionStatus.Inactive; Touch(); }
    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
