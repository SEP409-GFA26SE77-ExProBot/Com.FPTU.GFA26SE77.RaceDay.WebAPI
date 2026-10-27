using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Complaints;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Complaints;

public sealed record GetComplaintDetailQuery(Guid ComplaintId) : IRequest<EventComplaintDto?>;

public sealed class GetComplaintDetailQueryValidator : AbstractValidator<GetComplaintDetailQuery>
{
    public GetComplaintDetailQueryValidator() => RuleFor(x => x.ComplaintId).NotEmpty();
}

public sealed class GetComplaintDetailQueryHandler(
    IEventComplaintRepository complaints,
    IRaceEventRepository events,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetComplaintDetailQuery, EventComplaintDto?>
{
    public async Task<EventComplaintDto?> Handle(GetComplaintDetailQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var complaint = await complaints.GetByIdAsync(request.ComplaintId, cancellationToken);
        if (complaint is null) return null;

        var race = await events.GetByIdWithDetailsAsync(complaint.EventId, cancellationToken);
        if (race is null || (!currentUser.IsAdmin && complaint.UserId != userId && race.OrganizerUserId != userId))
            return null;

        return new EventComplaintDto(
            complaint.Id, complaint.EventId, race.Name, complaint.DistanceId,
            race.Distances.FirstOrDefault(x => x.Id == complaint.DistanceId)?.Name ?? "N/A",
            complaint.UserId, complaint.Title, complaint.Content, complaint.GetEvidenceLinks(),
            complaint.Status.ToString(), complaint.Status == ComplaintStatus.Pending ? "Unprocessed" : "Processed",
            complaint.ResponseContent, complaint.CreatedAtUtc, complaint.RepliedAtUtc);
    }
}
