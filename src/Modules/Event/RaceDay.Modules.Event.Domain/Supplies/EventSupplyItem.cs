using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Domain.Supplies;

/// <summary>Vật phẩm tiếp tế theo thùng — thuộc một giải (organizer inventory).</summary>
public sealed class EventSupplyItem : AggregateRoot
{
    public const string DefaultUnit = "thùng";
    public const int MaxNameLength = 200;
    public const int MaxUnitLength = 32;
    public const int MaxNotesLength = 1000;
    public const int MaxItemsPerEvent = 50;

    public Guid EventId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public string Unit { get; private set; } = DefaultUnit;
    public string? Notes { get; private set; }
    public int SortOrder { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private EventSupplyItem()
    {
    }

    public static EventSupplyItem Create(
        Guid eventId,
        string name,
        int quantity,
        string? unit = null,
        string? notes = null,
        int sortOrder = 0,
        Guid? id = null)
    {
        if (eventId == Guid.Empty)
        {
            throw new DomainException("Event không hợp lệ.");
        }

        var item = new EventSupplyItem
        {
            EventId = eventId,
            SortOrder = sortOrder,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        if (id is { } explicitId && explicitId != Guid.Empty)
        {
            item.Id = explicitId;
        }

        item.Apply(name, quantity, unit, notes);
        return item;
    }

    public void Update(string name, int quantity, string? unit, string? notes, int sortOrder)
    {
        Apply(name, quantity, unit, notes);
        SortOrder = sortOrder;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private void Apply(string name, int quantity, string? unit, string? notes)
    {
        var trimmedName = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new DomainException("Tên vật phẩm bắt buộc.");
        }

        if (trimmedName.Length > MaxNameLength)
        {
            throw new DomainException($"Tên vật phẩm tối đa {MaxNameLength} ký tự.");
        }

        if (quantity < 0)
        {
            throw new DomainException("Số lượng không được âm.");
        }

        var trimmedUnit = string.IsNullOrWhiteSpace(unit) ? DefaultUnit : unit.Trim();
        if (trimmedUnit.Length > MaxUnitLength)
        {
            throw new DomainException($"Đơn vị tối đa {MaxUnitLength} ký tự.");
        }

        string? trimmedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (trimmedNotes is { Length: > MaxNotesLength })
        {
            throw new DomainException($"Ghi chú tối đa {MaxNotesLength} ký tự.");
        }

        Name = trimmedName;
        Quantity = quantity;
        Unit = trimmedUnit;
        Notes = trimmedNotes;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
