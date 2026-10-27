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

// 1. Thêm tham số lọc và phân trang vào Query, đổi kiểu trả về sang dạng PagedDto
public sealed record ListOrganizerComplaintsQuery(
    Guid? EventId,
    string? Status,
    DateTime? FromDate,
    DateTime? ToDate,
    int Page,
    int PageSize) : IRequest<OrganizerComplaintsPagedDto>;

public sealed class ListOrganizerComplaintsQueryHandler(
    IRaceEventRepository events,
    IEventComplaintRepository complaints,
    ICurrentUserAccessor currentUser)
    : IRequestHandler<ListOrganizerComplaintsQuery, OrganizerComplaintsPagedDto>
{
    public async Task<OrganizerComplaintsPagedDto> Handle(ListOrganizerComplaintsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var targetEventIds = new List<Guid>();

        // 2. Xác định danh sách EventId hợp lệ mà Organizer/Admin được phép xem
        if (request.EventId.HasValue)
        {
            targetEventIds.Add(request.EventId.Value);
        }
        else if (!currentUser.IsAdmin)
        {
            // Nếu không phải Admin và không truyền EventId: Lấy các giải của Organizer này
            var myEvents = await events.ListByOrganizerAsync(userId, cancellationToken);
            targetEventIds = myEvents.Select(e => e.Id).ToList();

            // Nếu Organizer chưa có giải nào, trả về danh sách rỗng luôn
            if (targetEventIds.Count == 0)
            {
                return new OrganizerComplaintsPagedDto(new List<EventComplaintDto>(), request.Page, request.PageSize, 0, 0);
            }
        }

        // 3. Gọi DB thực hiện Lọc, Phân trang và Đếm số lượng
        var (dbItems, total, pending) = await complaints.SearchOrganizerComplaintsAsync(
            targetEventIds,
            currentUser.IsAdmin,
            request.Status,
            request.FromDate,
            request.ToDate,
            request.Page,
            request.PageSize,
            cancellationToken);

        var resultItems = new List<EventComplaintDto>();

        // 4. Map dữ liệu sang DTO (Bổ sung Title, EvidenceLinks, ProcessingStatus)
        foreach (var c in dbItems)
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

            resultItems.Add(new EventComplaintDto(
                c.Id,
                c.EventId,
                race?.Name ?? "N/A",
                c.DistanceId,
                distanceName,
                c.UserId,
                c.Title,
                c.Content,
                c.GetEvidenceLinks(),
                c.Status.ToString(),
                c.Status == Domain.Complaints.ComplaintStatus.Pending ? "Unprocessed" : "Processed",
                c.ResponseContent,
                c.CreatedAtUtc,
                c.RepliedAtUtc));
        }

        return new OrganizerComplaintsPagedDto(resultItems, request.Page, request.PageSize, total, pending);
    }
}