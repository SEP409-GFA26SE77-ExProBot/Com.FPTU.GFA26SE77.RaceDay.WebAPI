using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Complaints;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Complaints;

// 1. Thêm tham số Decision vào Command
public sealed record ReplyComplaintCommand(
    Guid ComplaintId,
    string ResponseContent,
    string Decision) : IRequest<EventComplaintDto>;

public sealed class ReplyComplaintCommandValidator : AbstractValidator<ReplyComplaintCommand>
{
    public ReplyComplaintCommandValidator()
    {
        RuleFor(x => x.ComplaintId).NotEmpty();
        RuleFor(x => x.ResponseContent).NotEmpty().MaximumLength(2000);

        // 2. Kiểm tra Decision chỉ được là Accepted hoặc Rejected
        RuleFor(x => x.Decision)
            .Must(x => string.Equals(x, "Accepted", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(x, "Rejected", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Quyết định (Decision) phải là 'Accepted' hoặc 'Rejected'.");
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

        // 3. Chuyển string Decision sang Enum ComplaintStatus
        var decision = Enum.Parse<ComplaintStatus>(request.Decision, true);

        // Gọi hàm Reply đã được cập nhật ở Domain
        complaint.Reply(userId, request.ResponseContent, decision);

        complaints.Update(complaint);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var distance = race.GetDistance(complaint.DistanceId);

        // 4. Trả về DTO với các trường mới
        return new EventComplaintDto(
            complaint.Id,
            race.Id,
            race.Name,
            distance.Id,
            distance.Name,
            complaint.UserId,
            complaint.Title,
            complaint.Content,
            complaint.GetEvidenceLinks(),
            complaint.Status.ToString().ToLowerInvariant(),
            "processed", // Giao diện cần trạng thái xử lý
            complaint.ResponseContent,
            complaint.CreatedAtUtc,
            complaint.RepliedAtUtc);
    }
}
