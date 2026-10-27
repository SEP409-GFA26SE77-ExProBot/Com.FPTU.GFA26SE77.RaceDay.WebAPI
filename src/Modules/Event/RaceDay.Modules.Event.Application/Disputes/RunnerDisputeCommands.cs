using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Disputes;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Disputes;

public sealed record CreateEventDisputeCommand(Guid EventId, string Title, string Description)
    : IRequest<EventDisputeDto>;

public sealed class CreateEventDisputeCommandValidator : AbstractValidator<CreateEventDisputeCommand>
{
    public CreateEventDisputeCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(EventDispute.MaxTitleLength);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(EventDispute.MaxDescriptionLength);
    }
}

public sealed class CreateEventDisputeCommandHandler(
    IRecentJoinedEventQuery recentEvents,
    IEventDisputeRepository disputes,
    IEventUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider) : IRequestHandler<CreateEventDisputeCommand, EventDisputeDto>
{
    public async Task<EventDisputeDto> Handle(CreateEventDisputeCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var race = await recentEvents.GetAsync(
            userId, request.EventId, DateOnly.FromDateTime(now), cancellationToken)
            ?? throw new DomainException(
                "Chỉ có thể khiếu nại giải đã tham gia trong vòng 1 tháng gần đây với đăng ký đã xác nhận hoặc đã hoàn thành.");

        var dispute = EventDispute.Create(race.Id, userId, request.Title, request.Description, now);
        await disputes.AddAsync(dispute, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return EventDisputeDto.From(dispute, race.Name);
    }
}

public sealed record ListMyEventDisputesQuery(int Page = 1, int PageSize = 10)
    : IRequest<RunnerPageDto<EventDisputeDto>>;

public sealed class ListMyEventDisputesQueryValidator : AbstractValidator<ListMyEventDisputesQuery>
{
    public ListMyEventDisputesQueryValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class ListMyEventDisputesQueryHandler(
    IEventDisputeRepository disputes,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListMyEventDisputesQuery, RunnerPageDto<EventDisputeDto>>
{
    public Task<RunnerPageDto<EventDisputeDto>> Handle(ListMyEventDisputesQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        return disputes.ListByRunnerAsync(userId, request.Page, request.PageSize, cancellationToken);
    }
}

public sealed record ListRecentJoinedEventsQuery(int Page = 1, int PageSize = 20)
    : IRequest<RunnerPageDto<RecentJoinedEventDto>>;

public sealed class ListRecentJoinedEventsQueryValidator : AbstractValidator<ListRecentJoinedEventsQuery>
{
    public ListRecentJoinedEventsQueryValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class ListRecentJoinedEventsQueryHandler(
    IRecentJoinedEventQuery recentEvents,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider) : IRequestHandler<ListRecentJoinedEventsQuery, RunnerPageDto<RecentJoinedEventDto>>
{
    public Task<RunnerPageDto<RecentJoinedEventDto>> Handle(ListRecentJoinedEventsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        return recentEvents.ListAsync(userId, today, request.Page, request.PageSize, cancellationToken);
    }
}
