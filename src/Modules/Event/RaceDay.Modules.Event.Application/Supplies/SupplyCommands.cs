using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Application.Staff;
using RaceDay.Modules.Event.Domain.Supplies;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Supplies;

public sealed record ListEventSuppliesQuery(Guid EventId) : IRequest<IReadOnlyList<EventSupplyItemDto>>;

public sealed class ListEventSuppliesQueryHandler(
    IRaceEventRepository events,
    IEventSupplyItemRepository supplies,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListEventSuppliesQuery, IReadOnlyList<EventSupplyItemDto>>
{
    public async Task<IReadOnlyList<EventSupplyItemDto>> Handle(
        ListEventSuppliesQuery request,
        CancellationToken cancellationToken)
    {
        await ListEventStaffQueryHandler.EnsureOrganizerAsync(
            events, currentUser, request.EventId, cancellationToken);

        var list = await supplies.ListByEventAsync(request.EventId, cancellationToken);
        return list.Select(ToDto).ToList();
    }

    internal static EventSupplyItemDto ToDto(EventSupplyItem x) =>
        new(x.Id.ToString("D"), x.Name, x.Quantity, x.Unit, x.Notes);

    internal static Guid? TryParseId(string? raw) =>
        Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
}

public sealed record ReplaceEventSuppliesCommand(
    Guid EventId,
    UpdateEventSuppliesRequest Body) : IRequest<IReadOnlyList<EventSupplyItemDto>>;

public sealed class ReplaceEventSuppliesCommandValidator : AbstractValidator<ReplaceEventSuppliesCommand>
{
    public ReplaceEventSuppliesCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.Body).NotNull();
        RuleFor(x => x.Body.Supplies).NotNull();
        RuleFor(x => x.Body.Supplies!.Count)
            .LessThanOrEqualTo(EventSupplyItem.MaxItemsPerEvent)
            .WithMessage($"Tối đa {EventSupplyItem.MaxItemsPerEvent} vật phẩm / giải.");
        RuleForEach(x => x.Body.Supplies).ChildRules(item =>
        {
            item.RuleFor(i => i.Name).NotEmpty().MaximumLength(EventSupplyItem.MaxNameLength);
            item.RuleFor(i => i.Quantity).GreaterThanOrEqualTo(0);
            item.RuleFor(i => i.Unit!).MaximumLength(EventSupplyItem.MaxUnitLength).When(i => i.Unit != null);
            item.RuleFor(i => i.Notes!).MaximumLength(EventSupplyItem.MaxNotesLength).When(i => i.Notes != null);
        });
    }
}

public sealed class ReplaceEventSuppliesCommandHandler(
    IRaceEventRepository events,
    IEventSupplyItemRepository supplies,
    IEventUnitOfWork uow,
    ICurrentUserAccessor currentUser)
    : IRequestHandler<ReplaceEventSuppliesCommand, IReadOnlyList<EventSupplyItemDto>>
{
    public async Task<IReadOnlyList<EventSupplyItemDto>> Handle(
        ReplaceEventSuppliesCommand request,
        CancellationToken cancellationToken)
    {
        await ListEventStaffQueryHandler.EnsureOrganizerAsync(
            events, currentUser, request.EventId, cancellationToken);

        await supplies.RemoveAllForEventAsync(request.EventId, cancellationToken);

        var order = 0;
        var created = new List<EventSupplyItem>();
        foreach (var row in request.Body.Supplies)
        {
            var item = EventSupplyItem.Create(
                request.EventId,
                row.Name,
                row.Quantity,
                row.Unit,
                row.Notes,
                order++,
                ListEventSuppliesQueryHandler.TryParseId(row.Id));
            await supplies.AddAsync(item, cancellationToken);
            created.Add(item);
        }

        await uow.SaveChangesAsync(cancellationToken);
        return created.Select(ListEventSuppliesQueryHandler.ToDto).ToList();
    }
}

public sealed record AddEventSupplyCommand(
    Guid EventId,
    CreateEventSupplyRequest Body) : IRequest<EventSupplyItemDto>;

public sealed class AddEventSupplyCommandValidator : AbstractValidator<AddEventSupplyCommand>
{
    public AddEventSupplyCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.Body).NotNull();
        RuleFor(x => x.Body.Name).NotEmpty().MaximumLength(EventSupplyItem.MaxNameLength);
        RuleFor(x => x.Body.Quantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Body.Unit!).MaximumLength(EventSupplyItem.MaxUnitLength).When(x => x.Body.Unit != null);
        RuleFor(x => x.Body.Notes!).MaximumLength(EventSupplyItem.MaxNotesLength).When(x => x.Body.Notes != null);
    }
}

public sealed class AddEventSupplyCommandHandler(
    IRaceEventRepository events,
    IEventSupplyItemRepository supplies,
    IEventUnitOfWork uow,
    ICurrentUserAccessor currentUser) : IRequestHandler<AddEventSupplyCommand, EventSupplyItemDto>
{
    public async Task<EventSupplyItemDto> Handle(
        AddEventSupplyCommand request,
        CancellationToken cancellationToken)
    {
        await ListEventStaffQueryHandler.EnsureOrganizerAsync(
            events, currentUser, request.EventId, cancellationToken);

        var existing = await supplies.ListByEventAsync(request.EventId, cancellationToken);
        if (existing.Count >= EventSupplyItem.MaxItemsPerEvent)
        {
            throw new DomainException($"Tối đa {EventSupplyItem.MaxItemsPerEvent} vật phẩm / giải.");
        }

        var sortOrder = existing.Count == 0 ? 0 : existing.Max(x => x.SortOrder) + 1;
        var item = EventSupplyItem.Create(
            request.EventId,
            request.Body.Name,
            request.Body.Quantity,
            request.Body.Unit,
            request.Body.Notes,
            sortOrder,
            ListEventSuppliesQueryHandler.TryParseId(request.Body.Id));
        await supplies.AddAsync(item, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        return ListEventSuppliesQueryHandler.ToDto(item);
    }
}

public sealed record DeleteEventSupplyCommand(Guid EventId, Guid SupplyId) : IRequest;

public sealed class DeleteEventSupplyCommandHandler(
    IRaceEventRepository events,
    IEventSupplyItemRepository supplies,
    IEventUnitOfWork uow,
    ICurrentUserAccessor currentUser) : IRequestHandler<DeleteEventSupplyCommand>
{
    public async Task Handle(DeleteEventSupplyCommand request, CancellationToken cancellationToken)
    {
        await ListEventStaffQueryHandler.EnsureOrganizerAsync(
            events, currentUser, request.EventId, cancellationToken);

        var item = await supplies.GetByIdAsync(request.SupplyId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy vật phẩm.");
        if (item.EventId != request.EventId)
        {
            throw new DomainException("Vật phẩm không thuộc giải này.");
        }

        supplies.Remove(item);
        await uow.SaveChangesAsync(cancellationToken);
    }
}
