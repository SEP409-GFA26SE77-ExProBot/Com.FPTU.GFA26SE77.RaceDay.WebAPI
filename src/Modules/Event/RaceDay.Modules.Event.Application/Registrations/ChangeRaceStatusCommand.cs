using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Modules.Event.Domain.Staff;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Registrations;

public sealed record ChangeRaceStatusRequest(string Status, string Reason);
public sealed record ChangeRaceStatusCommand(Guid EventId, Guid RegistrationId, string Status, string Reason)
    : IRequest<RaceStatusChangeDto>;
public sealed record RaceStatusChangeDto(Guid RegistrationId, Guid EventId, string? BibNumber,
    string PreviousStatus, string RaceStatus, string Reason, DateTime ChangedAtUtc, Guid ChangedByUserId);

public sealed class ChangeRaceStatusCommandValidator : AbstractValidator<ChangeRaceStatusCommand>
{
    public ChangeRaceStatusCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.RegistrationId).NotEmpty();
        RuleFor(x => x.Status).Must(x => x?.Trim().ToLowerInvariant() is "dns" or "dnf" or "dq")
            .WithMessage("Trạng thái phải là dns, dnf hoặc dq.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class ChangeRaceStatusCommandHandler(
    IRaceEventRepository events,
    IEventRegistrationRepository registrations,
    IEventStaffMemberRepository staff,
    IRaceStatusChangeRepository changes,
    IEventUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    TimeProvider clock) : IRequestHandler<ChangeRaceStatusCommand, RaceStatusChangeDto>
{
    public async Task<RaceStatusChangeDto> Handle(ChangeRaceStatusCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        if (userId == Guid.Empty) throw new DomainException("Cần đăng nhập.");
        var race = await events.GetByIdWithDetailsAsync(request.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");
        if (!currentUser.IsAdmin && race.OrganizerUserId != userId)
        {
            var assignments = await staff.ListActiveByUserIdAsync(userId, cancellationToken);
            if (!assignments.Any(x => x.EventId == request.EventId && x.UserId == userId
                && x.Status == EventStaffStatus.Active
                && x.Role is EventStaffRole.Volunteer or EventStaffRole.Coordinator))
                throw new DomainException("Bạn không có quyền cập nhật trạng thái thi đấu của giải này.");
        }

        var reg = await registrations.GetByIdAsync(request.RegistrationId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy đăng ký.");
        if (reg.EventId != request.EventId)
            throw new DomainException("Đăng ký không thuộc giải này.");
        if (reg.Status is not (RegistrationStatus.Confirmed or RegistrationStatus.Finished))
            throw new DomainException("Chỉ đăng ký đã được duyệt mới được cập nhật trạng thái thi đấu.");

        var target = request.Status?.Trim().ToLowerInvariant() switch
        {
            "dns" => RaceStatus.DNS,
            "dnf" => RaceStatus.DNF,
            "dq" => RaceStatus.DQ,
            _ => throw new DomainException("Trạng thái phải là dns, dnf hoặc dq.")
        };
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000)
            throw new DomainException("Lý do bắt buộc và không được vượt quá 1000 ký tự.");

        if (reg.RaceStatus == target)
        {
            var previous = await changes.GetLatestAsync(reg.Id, cancellationToken);
            if (previous is not null && previous.Status == target && previous.Reason == request.Reason.Trim())
                return ToDto(reg, previous);
            throw new DomainException("Không thể thay đổi lý do của quyết định đã ghi nhận.");
        }

        // Match the existing finish calculation: EventDate + distance/event start time are UTC.
        var startTime = race.GetDistance(reg.DistanceId).StartAt ?? race.StartTime;
        DateTime? scheduledStart = startTime.HasValue
            ? DateTime.SpecifyKind(race.EventDate.ToDateTime(startTime.Value), DateTimeKind.Utc)
            : null;
        var change = reg.ChangeRaceStatus(target, request.Reason, userId,
            clock.GetUtcNow().UtcDateTime, scheduledStart);
        registrations.Update(reg);
        await changes.AddAsync(change, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(reg, change);
    }

    private static RaceStatusChangeDto ToDto(EventRegistration reg, RaceStatusChange change) => new(
        reg.Id, reg.EventId, reg.BibNumber, RegistrationDtoMapper.ToApiRaceStatus(change.PreviousStatus),
        RegistrationDtoMapper.ToApiRaceStatus(change.Status), change.Reason, change.ChangedAtUtc, change.ChangedByUserId);
}
