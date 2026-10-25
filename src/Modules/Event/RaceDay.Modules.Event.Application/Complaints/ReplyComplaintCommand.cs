using System;
using System.Collections.Generic;
using System.Text;

using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Complaints;

public sealed record ReplyComplaintCommand(Guid ComplaintId, string ResponseContent) : IRequest<EventComplaintDto>;

public sealed class ReplyComplaintCommandValidator : AbstractValidator<ReplyComplaintCommand>
{
    public ReplyComplaintCommandValidator()
    {
        RuleFor(x => x.ComplaintId).NotEmpty();
        RuleFor(x => x.ResponseContent).NotEmpty().MaximumLength(2000);
    }
}

public sealed class ReplyComplaintCommandHandler(
    IRaceEventRepository events,
    IEventComplaintRepository complaints,
    ICurrentUserAccessor currentUser,
    IEventUnitOfWork unitOfWork) : IRequestHandler<ReplyComplaintCommand, EventComplaintDto>
{
    public async Task<EventComplaintDto> Handle(ReplyComplaintCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");

        var complaint = await complaints.GetByIdAsync(request.ComplaintId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy khiếu nại.");

        var race = await events.GetByIdWithDetailsAsync(complaint.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải đấu.");

        if (!currentUser.IsAdmin && race.OrganizerUserId != userId)
        {
            throw new DomainException("Bạn không phải chủ giải này.");
        }

        complaint.Reply(userId, request.ResponseContent);
        complaints.Update(complaint);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var distance = race.GetDistance(complaint.DistanceId);

        return new EventComplaintDto(
            complaint.Id,
            race.Id,
            race.Name,
            distance.Id,
            distance.Name,
            complaint.UserId,
            complaint.Content,
            complaint.Status.ToString().ToLowerInvariant(),
            complaint.ResponseContent,
            complaint.CreatedAtUtc,
            complaint.RepliedAtUtc);
    }
}