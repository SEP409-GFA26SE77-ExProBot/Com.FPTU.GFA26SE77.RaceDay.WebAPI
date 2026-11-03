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

public sealed record CreateComplaintCommand(
    Guid EventId,
    Guid DistanceId,
    string Title,
    string Content,
    IReadOnlyList<string>? EvidenceLinks) : IRequest<EventComplaintDto>;

public sealed class CreateComplaintCommandValidator : AbstractValidator<CreateComplaintCommand>
{
    public CreateComplaintCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.DistanceId).NotEmpty();

        // Validate cho Title mới thêm
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);

        RuleFor(x => x.Content).NotEmpty().MaximumLength(2000);

        // Validate giới hạn tối đa 5 link bằng chứng
        RuleFor(x => x.EvidenceLinks)
            .Must(x => x is { Count: >= 1 and <= 5 })
            .WithMessage("Cần từ 1 đến 5 link bằng chứng.");
        RuleForEach(x => x.EvidenceLinks).Must(link =>
            Uri.TryCreate(link, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            .WithMessage("Link bằng chứng phải là URL http hoặc https hợp lệ.");
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

        // Khởi tạo entity kèm Title và EvidenceLinks
        var complaint = EventComplaint.Create(
            race.Id,
            distance.Id,
            userId,
            request.Title,
            request.Content,
            request.EvidenceLinks);

        await complaints.AddAsync(complaint, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Trả về DTO đã được cập nhật thêm trường Title, EvidenceLinks và ProcessingStatus
        return new EventComplaintDto(
            complaint.Id,
            race.Id,
            race.Name,
            distance.Id,
            distance.Name,
            userId,
            complaint.Title,
            complaint.Content,
            complaint.GetEvidenceLinks(),
            complaint.Status.ToString().ToLowerInvariant(),
            "unprocessed", // Vì đơn mới tạo luôn ở trạng thái Pending (Chưa xử lý)
            complaint.ResponseContent,
            complaint.CreatedAtUtc,
            complaint.RepliedAtUtc);
    }
}
