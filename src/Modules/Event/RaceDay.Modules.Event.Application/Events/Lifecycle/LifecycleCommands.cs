using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Events;

public sealed record SubmitRaceEventCommand(Guid EventId) : IRequest<RaceEventDto>;

public sealed class SubmitRaceEventCommandHandler(
    IRaceEventRepository events,
    ICurrentUserAccessor currentUser,
    IEventUnitOfWork unitOfWork,
    IEventBus eventBus) : IRequestHandler<SubmitRaceEventCommand, RaceEventDto>
{
    public async Task<RaceEventDto> Handle(SubmitRaceEventCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var race = await events.GetByIdWithDetailsAsync(request.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        if (!currentUser.IsAdmin && race.OrganizerUserId != userId)
        {
            throw new DomainException("Bạn không phải chủ giải này.");
        }

        race.SubmitForApproval();
        events.Update(race);
        await eventBus.PublishAsync(
            new RaceEventSubmittedIntegrationEvent(race.Id, race.Name, race.OrganizerUserId),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RaceEventDtoMapper.ToDetail(race);
    }
}

public sealed record ApproveRaceEventCommand(Guid EventId, bool OpenRegistration = false) : IRequest<RaceEventDto>;

public sealed class ApproveRaceEventCommandHandler(
    IRaceEventRepository events,
    IEventUnitOfWork unitOfWork,
    IEventBus eventBus) : IRequestHandler<ApproveRaceEventCommand, RaceEventDto>
{
    public async Task<RaceEventDto> Handle(ApproveRaceEventCommand request, CancellationToken cancellationToken)
    {
        var race = await events.GetByIdWithDetailsAsync(request.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        race.Approve();
        if (request.OpenRegistration)
        {
            race.OpenRegistration();
        }

        events.Update(race);
        await eventBus.PublishAsync(
            new RaceEventApprovedIntegrationEvent(race.Id, race.Name, race.OrganizerUserId),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RaceEventDtoMapper.ToDetail(race);
    }
}

public sealed record RejectRaceEventCommand(Guid EventId, string Reason) : IRequest<RaceEventDto>;

public sealed class RejectRaceEventCommandValidator : AbstractValidator<RejectRaceEventCommand>
{
    public RejectRaceEventCommandValidator()
        => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
}

public sealed class RejectRaceEventCommandHandler(
    IRaceEventRepository events,
    IEventUnitOfWork unitOfWork,
    IEventBus eventBus) : IRequestHandler<RejectRaceEventCommand, RaceEventDto>
{
    public async Task<RaceEventDto> Handle(RejectRaceEventCommand request, CancellationToken cancellationToken)
    {
        var race = await events.GetByIdWithDetailsAsync(request.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        race.Reject(request.Reason);
        events.Update(race);
        await eventBus.PublishAsync(
            new RaceEventRejectedIntegrationEvent(race.Id, race.Name, race.OrganizerUserId, request.Reason),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RaceEventDtoMapper.ToDetail(race);
    }
}

public sealed record OpenRegistrationCommand(Guid EventId) : IRequest<RaceEventDto>;

public sealed class OpenRegistrationCommandHandler(
    IRaceEventRepository events,
    ICurrentUserAccessor currentUser,
    IEventUnitOfWork unitOfWork) : IRequestHandler<OpenRegistrationCommand, RaceEventDto>
{
    public async Task<RaceEventDto> Handle(OpenRegistrationCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var race = await events.GetByIdWithDetailsAsync(request.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        if (!currentUser.IsAdmin && race.OrganizerUserId != userId)
        {
            throw new DomainException("Bạn không phải chủ giải này.");
        }

        race.OpenRegistration();
        events.Update(race);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RaceEventDtoMapper.ToDetail(race);
    }
}

public sealed record CloseRegistrationCommand(Guid EventId) : IRequest<RaceEventDto>;

public sealed class CloseRegistrationCommandHandler(
    IRaceEventRepository events,
    ICurrentUserAccessor currentUser,
    IEventUnitOfWork unitOfWork) : IRequestHandler<CloseRegistrationCommand, RaceEventDto>
{
    public async Task<RaceEventDto> Handle(CloseRegistrationCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var race = await events.GetByIdWithDetailsAsync(request.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        if (!currentUser.IsAdmin && race.OrganizerUserId != userId)
        {
            throw new DomainException("Bạn không phải chủ giải này.");
        }

        race.CloseRegistration();
        events.Update(race);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RaceEventDtoMapper.ToDetail(race);
    }
}
