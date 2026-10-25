using System;
using System.Collections.Generic;
using System.Text;
using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Complaints;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Complaints;

public sealed record CreateComplaintCommand(
    Guid EventId,
    Guid DistanceId,
    string Content) : IRequest<EventComplaintDto>;

public sealed class CreateComplaintCommandValidator : AbstractValidator<CreateComplaintCommand>
{
    public CreateComplaintCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.DistanceId).NotEmpty();
        RuleFor(x => x.Content).NotEmpty().MaximumLength(2000);
    }
}

public sealed class CreateComplaintCommandHandler(
    IRaceEventRepository events,
    IEventComplaintRepository complaints,
    ICurrentUserAccessor currentUser,
    IEventUnitOfWork unitOfWork) : IRequestHandler<CreateComplaintCommand, EventComplaintDto>
{
    public async Task<EventComplaintDto> Handle(CreateComplaintCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập để gửi khiếu nại.");

        var race = await events.GetByIdWithDetailsAsync(request.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải đấu.");

        var distance = race.GetDistance(request.DistanceId);

        var complaint = EventComplaint.Create(race.Id, distance.Id, userId, request.Content);

        await complaints.AddAsync(complaint, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new EventComplaintDto(
            complaint.Id,
            race.Id,
            race.Name,
            distance.Id,
            distance.Name,
            userId,
            complaint.Content,
            complaint.Status.ToString().ToLowerInvariant(),
            complaint.ResponseContent,
            complaint.CreatedAtUtc,
            complaint.RepliedAtUtc);
    }
}