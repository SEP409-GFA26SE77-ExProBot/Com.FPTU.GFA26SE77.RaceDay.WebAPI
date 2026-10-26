using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Domain.Abstractions;

namespace RaceDay.Modules.RaceMonitoring.Domain.AidStations;

/// <summary>Catalogue entry for a supply tracked at aid stations.</summary>
public sealed class SupplyItem : AggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public string Unit { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; } = true;

    private SupplyItem()
    {
    }

    public static SupplyItem Create(string name, string unit, string? description = null, bool isActive = true)
    {
        return new SupplyItem
        {
            Name = CoordinatorInvariants.RequiredText(
                name,
                CoordinatorFieldLengths.Name,
                "Tên vật tư không được để trống.",
                "Tên vật tư tối đa 200 ký tự."),
            Unit = CoordinatorInvariants.RequiredText(
                unit,
                CoordinatorFieldLengths.Unit,
                "Đơn vị vật tư không được để trống.",
                "Đơn vị vật tư tối đa 30 ký tự."),
            Description = CoordinatorInvariants.OptionalLongText(description),
            IsActive = isActive
        };
    }
}
