using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using FluentValidation;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Complaints;

// 1. Thêm tham số lọc, phân trang và đổi kiểu trả về sang MyComplaintsPagedDto
public sealed record GetMyComplaintsQuery(
    Guid? EventId,
    string? Status,
    int Page,
    int PageSize) : IRequest<MyComplaintsPagedDto>;

public sealed class GetMyComplaintsQueryValidator : AbstractValidator<GetMyComplaintsQuery>
{
    public GetMyComplaintsQueryValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Status).Must(ComplaintFilterValidation.IsValidStatus)
            .WithMessage("Trạng thái phải là Pending, Accepted hoặc Rejected.");
    }
}

internal static class ComplaintFilterValidation
{
    public static bool IsValidStatus(string? status) => string.IsNullOrWhiteSpace(status)
        || string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Accepted", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase);
}

public sealed class GetMyComplaintsQueryHandler(
    IEventComplaintRepository complaints,
    IRaceEventRepository events,
    ICurrentUserAccessor currentUser)
    : IRequestHandler<GetMyComplaintsQuery, MyComplaintsPagedDto>
{
    public async Task<MyComplaintsPagedDto> Handle(GetMyComplaintsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");

        // 2. Gọi DB thực hiện Lọc và Phân trang thay vì load tất cả lên memory
        var (dbItems, total) = await complaints.SearchMyComplaintsAsync(
            userId,
            request.EventId,
            request.Status,
            request.Page,
            request.PageSize,
            cancellationToken);

        var resultItems = new List<EventComplaintDto>();

        // 3. Map dữ liệu sang DTO (Bổ sung Title, EvidenceLinks, ProcessingStatus)
        foreach (var c in dbItems)
        {
            var race = await events.GetByIdWithDetailsAsync(c.EventId, cancellationToken);
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
                race?.Name ?? "Unknown Event",
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

        return new MyComplaintsPagedDto(resultItems, request.Page, request.PageSize, total);
    }
}
