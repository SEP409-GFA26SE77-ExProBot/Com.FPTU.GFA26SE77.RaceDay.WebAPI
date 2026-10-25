using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Complaints;

public sealed record ListOrganizerComplaintsQuery(Guid? EventId = null)
    : IRequest<IReadOnlyList<OrganizerEventComplaintsDto>>;

public sealed class ListOrganizerComplaintsQueryHandler(
    IRaceEventRepository events,
    IEventComplaintRepository complaints,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListOrganizerComplaintsQuery, IReadOnlyList<OrganizerEventComplaintsDto>>
{
    public async Task<IReadOnlyList<OrganizerEventComplaintsDto>> Handle(
        ListOrganizerComplaintsQuery request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");

        List<RaceEvent> targetEvents = new();

        // 1. Nếu là Admin
        if (currentUser.IsAdmin)
        {
            if (request.EventId.HasValue)
            {
                // Nếu Admin truyền eventId cụ thể thì chỉ lấy giải đó
                var ev = await events.GetByIdAsync(request.EventId.Value, cancellationToken);
                if (ev != null) targetEvents.Add(ev);
            }
            else
            {
                // Nếu Admin để trống eventId: Lấy toàn bộ các giải có trong hệ thống thông qua các khiếu nại hoặc qua danh sách giải
                // Ta có thể lấy tất cả các khiếu nại bằng cách truyền null hoặc xử lý lấy tất cả giải.
                // Ở đây ta gom tất cả các EventId đang có khiếu nại, HOẶC nếu repository của bạn hỗ trợ lấy tất cả giải thì tuyệt vời.
                // Cách an toàn nhất là lấy tất cả complaints từ DB trước:
                var allComplaints = await complaints.ListByEventIdsAsync(null!, cancellationToken);
                var activeEventIds = allComplaints.Select(c => c.EventId).Distinct().ToList();

                foreach (var eventId in activeEventIds)
                {
                    var ev = await events.GetByIdAsync(eventId, cancellationToken);
                    if (ev != null) targetEvents.Add(ev);
                }
            }
        }
        else
        {
            // 2. Logic dành cho Organizer thông thường (chỉ xem giải của mình)
            var myEvents = await events.ListByOrganizerAsync(userId, cancellationToken);
            if (myEvents.Count == 0)
            {
                return Array.Empty<OrganizerEventComplaintsDto>();
            }

            if (request.EventId.HasValue)
            {
                targetEvents = myEvents.Where(e => e.Id == request.EventId.Value).ToList();
            }
            else
            {
                targetEvents = myEvents.ToList();
            }
        }

        if (targetEvents.Count == 0)
        {
            return Array.Empty<OrganizerEventComplaintsDto>();
        }

        var eventIds = targetEvents.Select(e => e.Id).ToList();
        var rawComplaints = await complaints.ListByEventIdsAsync(eventIds, cancellationToken);

        var result = new List<OrganizerEventComplaintsDto>();

        foreach (var raceSummary in targetEvents)
        {
            var race = await events.GetByIdWithDetailsAsync(raceSummary.Id, cancellationToken);
            if (race is null) continue;

            var eventComplaints = rawComplaints
                .Where(c => c.EventId == race.Id)
                .Select(c =>
                {
                    string distanceName = "N/A";
                    try
                    {
                        distanceName = race.GetDistance(c.DistanceId).Name;
                    }
                    catch
                    {
                        /* Ignore missing distance */
                    }

                    return new EventComplaintDto(
                        c.Id,
                        race.Id,
                        race.Name,
                        c.DistanceId,
                        distanceName,
                        c.UserId,
                        c.Content,
                        c.Status.ToString().ToLowerInvariant(),
                        c.ResponseContent,
                        c.CreatedAtUtc,
                        c.RepliedAtUtc);
                })
                .OrderByDescending(c => c.CreatedAtUtc)
                .ToList();

            if (eventComplaints.Count > 0)
            {
                result.Add(new OrganizerEventComplaintsDto(race.Id, race.Name, eventComplaints));
            }
        }

        return result;
    }
}