using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Complaints;

public sealed record GetMyComplaintsQuery : IRequest<IReadOnlyList<EventComplaintDto>>;

public sealed class GetMyComplaintsQueryHandler(
    IEventComplaintRepository complaints,
    IRaceEventRepository events,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetMyComplaintsQuery, IReadOnlyList<EventComplaintDto>>
{
    public async Task<IReadOnlyList<EventComplaintDto>> Handle(GetMyComplaintsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");

        // Lấy danh sách khiếu nại của riêng user đang đăng nhập
        var myComplaints = await complaints.ListByUserAsync(userId, cancellationToken);
        var result = new List<EventComplaintDto>();

        foreach (var c in myComplaints.OrderByDescending(x => x.CreatedAtUtc))
        {
            var race = await events.GetByIdAsync(c.EventId, cancellationToken);
            string distanceName = "N/A";
            try
            {
                distanceName = race?.GetDistance(c.DistanceId).Name ?? "N/A";
            }
            catch
            {
                /* Ignore missing distance */
            }

            result.Add(new EventComplaintDto(
                c.Id,
                c.EventId,
                race?.Name ?? "Unknown Event",
                c.DistanceId,
                distanceName,
                c.UserId,
                c.Content,
                c.Status.ToString().ToLowerInvariant(),
                c.ResponseContent,
                c.CreatedAtUtc,
                c.RepliedAtUtc));
        }

        return result;
    }
}