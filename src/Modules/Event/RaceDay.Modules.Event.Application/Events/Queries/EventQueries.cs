using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Events;

public sealed record ListPublicEventsQuery : IRequest<IReadOnlyList<RaceEventListItemDto>>;

public sealed class ListPublicEventsQueryHandler(IRaceEventRepository events)
    : IRequestHandler<ListPublicEventsQuery, IReadOnlyList<RaceEventListItemDto>>
{
    public async Task<IReadOnlyList<RaceEventListItemDto>> Handle(ListPublicEventsQuery request, CancellationToken cancellationToken)
    {
        var items = await events.ListPublicAsync(cancellationToken);
        return items.Select(RaceEventDtoMapper.ToListItem).ToList();
    }
}

public sealed record GetRaceEventByIdQuery(Guid EventId) : IRequest<RaceEventDto>;

public sealed class GetRaceEventByIdQueryHandler(IRaceEventRepository events)
    : IRequestHandler<GetRaceEventByIdQuery, RaceEventDto>
{
    public async Task<RaceEventDto> Handle(GetRaceEventByIdQuery request, CancellationToken cancellationToken)
    {
        var race = await events.GetByIdWithDetailsAsync(request.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");
        return RaceEventDtoMapper.ToDetail(race);
    }
}

public sealed record ListPendingEventsQuery : IRequest<IReadOnlyList<RaceEventDto>>;

public sealed class ListPendingEventsQueryHandler(IRaceEventRepository events)
    : IRequestHandler<ListPendingEventsQuery, IReadOnlyList<RaceEventDto>>
{
    public async Task<IReadOnlyList<RaceEventDto>> Handle(ListPendingEventsQuery request, CancellationToken cancellationToken)
    {
        var items = await events.ListByStatusAsync(RaceEventStatus.PendingApproval, cancellationToken);
        return items.Select(RaceEventDtoMapper.ToDetail).ToList();
    }
}

public sealed record ListOrganizerEventsQuery : IRequest<IReadOnlyList<RaceEventListItemDto>>;

public sealed class ListOrganizerEventsQueryHandler(
    IRaceEventRepository events,
    RaceDay.Shared.Application.Abstractions.ICurrentUserAccessor currentUser)
    : IRequestHandler<ListOrganizerEventsQuery, IReadOnlyList<RaceEventListItemDto>>
{
    public async Task<IReadOnlyList<RaceEventListItemDto>> Handle(ListOrganizerEventsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var items = await events.ListByOrganizerAsync(userId, cancellationToken);
        return items.Select(RaceEventDtoMapper.ToListItem).ToList();
    }
}
