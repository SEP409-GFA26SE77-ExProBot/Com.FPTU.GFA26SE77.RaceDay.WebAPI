using MediatR;
using RaceDay.Modules.AuditLog.Application.Abstractions;

namespace RaceDay.Modules.AuditLog.Application.AuditEntries;

public sealed record AuditEntryDto(
    Guid Id,
    string Action,
    string ActorId,
    string ResourceType,
    string ResourceId,
    string PayloadJson,
    DateTime OccurredOnUtc);

public sealed record PagedAuditEntriesResult(
    IReadOnlyList<AuditEntryDto> Items,
    long Total,
    int Page,
    int PageSize);

/// <summary>Legacy: N bản ghi gần nhất (không phân trang).</summary>
public sealed record ListRecentAuditEntriesQuery(int Take = 50) : IRequest<IReadOnlyList<AuditEntryDto>>;

public sealed class ListRecentAuditEntriesQueryHandler(IAuditEntryRepository repository)
    : IRequestHandler<ListRecentAuditEntriesQuery, IReadOnlyList<AuditEntryDto>>
{
    public async Task<IReadOnlyList<AuditEntryDto>> Handle(
        ListRecentAuditEntriesQuery request,
        CancellationToken cancellationToken)
    {
        var take = request.Take is < 1 or > 200 ? 50 : request.Take;
        var entries = await repository.ListRecentAsync(take, cancellationToken);
        return entries.Select(ToDto).ToList();
    }

    internal static AuditEntryDto ToDto(Domain.AuditEntries.AuditEntry x) => new(
        x.Id,
        x.Action,
        x.ActorId,
        x.ResourceType,
        x.ResourceId,
        x.PayloadJson,
        x.OccurredOnUtc);
}

public sealed record ListPagedAuditEntriesQuery(int Page = 1, int PageSize = 10)
    : IRequest<PagedAuditEntriesResult>;

public sealed class ListPagedAuditEntriesQueryHandler(IAuditEntryRepository repository)
    : IRequestHandler<ListPagedAuditEntriesQuery, PagedAuditEntriesResult>
{
    public async Task<PagedAuditEntriesResult> Handle(
        ListPagedAuditEntriesQuery request,
        CancellationToken cancellationToken)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? 10 : request.PageSize;
        var (items, total) = await repository.ListPagedAsync(page, pageSize, cancellationToken);
        return new PagedAuditEntriesResult(
            items.Select(ListRecentAuditEntriesQueryHandler.ToDto).ToList(),
            total,
            page,
            pageSize);
    }
}
