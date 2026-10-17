using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Staff;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Staff;

public sealed record StaffAssignmentDto(
    Guid EventId,
    string EventName,
    string Role,
    string Status,
    string? StationId,
    string? StationLabel,
    DateOnly? EventDate);

public sealed record ListMyStaffAssignmentsQuery : IRequest<IReadOnlyList<StaffAssignmentDto>>;

public sealed class ListMyStaffAssignmentsQueryHandler(
    IEventStaffMemberRepository staff,
    IRaceEventRepository events,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListMyStaffAssignmentsQuery, IReadOnlyList<StaffAssignmentDto>>
{
    public async Task<IReadOnlyList<StaffAssignmentDto>> Handle(
        ListMyStaffAssignmentsQuery request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var members = await staff.ListActiveByUserIdAsync(userId, cancellationToken);
        if (members.Count == 0)
        {
            return [];
        }

        var result = new List<StaffAssignmentDto>(members.Count);
        foreach (var m in members)
        {
            var race = await events.GetByIdAsync(m.EventId, cancellationToken);
            if (race is null)
            {
                continue;
            }

            result.Add(new StaffAssignmentDto(
                m.EventId,
                race.Name,
                EventStaffMapper.ToRoleApi(m.Role),
                EventStaffMapper.ToStatusApi(m.Status),
                m.StationId,
                m.StationLabel,
                race.EventDate));
        }

        return result;
    }
}
