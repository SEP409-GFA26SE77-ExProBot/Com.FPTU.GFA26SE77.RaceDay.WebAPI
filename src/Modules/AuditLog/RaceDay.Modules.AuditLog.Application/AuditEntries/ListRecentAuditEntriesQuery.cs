using MediatR;
using RaceDay.Modules.AuditLog.Application.Abstractions;

namespace RaceDay.Modules.AuditLog.Application.AuditEntries;

public sealed record AuditEntryDto(
    Guid Id,
    string Action,
    string ActorId,
    string ResourceType,
    string ResourceId,
    DateTime OccurredOnUtc);

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
        return entries
            .Select(x => new AuditEntryDto(
                x.Id,
                x.Action,
                x.ActorId,
                x.ResourceType,
                x.ResourceId,
                x.OccurredOnUtc))
            .ToList();
    }
}
