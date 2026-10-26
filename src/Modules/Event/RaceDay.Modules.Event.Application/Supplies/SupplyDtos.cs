namespace RaceDay.Modules.Event.Application.Supplies;

/// <summary>Id trả về dạng Guid string (FE EventSupplyItemDto.id: string).</summary>
public sealed record EventSupplyItemDto(
    string Id,
    string Name,
    int Quantity,
    string Unit,
    string? Notes);

public sealed record UpdateEventSuppliesRequest(IReadOnlyList<EventSupplyItemUpsertDto> Supplies);

/// <summary>Id tùy chọn: Guid hợp lệ thì giữ; FE có thể gửi "sup-…" → BE sinh Guid mới.</summary>
public sealed record EventSupplyItemUpsertDto(
    string? Id,
    string Name,
    int Quantity,
    string? Unit,
    string? Notes);

public sealed record CreateEventSupplyRequest(
    string? Id,
    string Name,
    int Quantity,
    string? Unit,
    string? Notes);
