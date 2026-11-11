using RaceDay.Modules.Map.Contracts.Courses;
using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Staff;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Staff;

public sealed record EventStaffMemberDto(
    Guid Id,
    Guid EventId,
    string Role,
    string FullName,
    string Email,
    string? Phone,
    string Status,
    string? StationId,
    string? StationLabel,
    DateTime CreatedAt);

public sealed record InviteEventStaffRequest(
    string Role,
    string Email,
    string FullName,
    string? Phone,
    string? StationId,
    string? StationLabel);

public sealed record AddEventStaffRequest(
    string Role,
    string FullName,
    string? Email,
    string? Phone,
    string? StationId,
    string? StationLabel);

public sealed record AssignEventStaffStationRequest(string? StationId, string? StationLabel);

public sealed record ListEventStaffQuery(Guid EventId, string? Role) : IRequest<IReadOnlyList<EventStaffMemberDto>>;
public sealed record InviteEventStaffCommand(Guid EventId, InviteEventStaffRequest Body) : IRequest<EventStaffMemberDto>;
public sealed record AddEventStaffCommand(Guid EventId, AddEventStaffRequest Body) : IRequest<EventStaffMemberDto>;
public sealed record AssignEventStaffStationCommand(Guid EventId, Guid MemberId, AssignEventStaffStationRequest Body)
    : IRequest<EventStaffMemberDto>;
public sealed record RevokeEventStaffCommand(Guid EventId, Guid MemberId) : IRequest;

internal static class EventStaffMapper
{
    public static EventStaffMemberDto ToDto(EventStaffMember m) => new(
        m.Id,
        m.EventId,
        ToRoleApi(m.Role),
        m.FullName,
        m.Email,
        m.Phone,
        ToStatusApi(m.Status),
        m.StationId,
        m.StationLabel,
        m.CreatedAtUtc);

    public static EventStaffRole ParseRole(string? role) => (role ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "race-staff" or "racestaff" or "race_staff"
            or "coordinator" or "volunteer" => EventStaffRole.RaceStaff,
        "photographer" => EventStaffRole.Photographer,
        _ => throw new DomainException("Role nhân sự không hợp lệ (race-staff|photographer).")
    };

    public static string ToRoleApi(EventStaffRole role) => role switch
    {
        EventStaffRole.RaceStaff => "race-staff",
        EventStaffRole.Photographer => "photographer",
        _ => role.ToString().ToLowerInvariant()
    };

    public static string ToStatusApi(EventStaffStatus status) => status switch
    {
        EventStaffStatus.Invited => "invited",
        EventStaffStatus.Active => "active",
        EventStaffStatus.Revoked => "revoked",
        _ => status.ToString().ToLowerInvariant()
    };
}

public sealed class InviteEventStaffCommandValidator : AbstractValidator<InviteEventStaffCommand>
{
    public InviteEventStaffCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.Body.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Body.Role).NotEmpty();
    }
}

public sealed class AddEventStaffCommandValidator : AbstractValidator<AddEventStaffCommand>
{
    public AddEventStaffCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.Body.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Body.Role).NotEmpty();
    }
}

public sealed class ListEventStaffQueryHandler(
    IRaceEventRepository events,
    IEventStaffMemberRepository staff,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListEventStaffQuery, IReadOnlyList<EventStaffMemberDto>>
{
    public async Task<IReadOnlyList<EventStaffMemberDto>> Handle(ListEventStaffQuery request, CancellationToken cancellationToken)
    {
        await EnsureOrganizerAsync(events, currentUser, request.EventId, cancellationToken);
        EventStaffRole? role = null;
        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            role = EventStaffMapper.ParseRole(request.Role);
        }

        var list = await staff.ListByEventAsync(request.EventId, role, cancellationToken);
        return list.Select(EventStaffMapper.ToDto).ToList();
    }

    internal static async Task EnsureOrganizerAsync(
        IRaceEventRepository events,
        ICurrentUserAccessor currentUser,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var race = await events.GetByIdAsync(eventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");
        if (!currentUser.IsAdmin && race.OrganizerUserId != userId)
        {
            throw new DomainException("Bạn không phải chủ giải này.");
        }
    }
}

public sealed class InviteEventStaffCommandHandler(
    IRaceCourseReadQuery courses,
    IRaceEventRepository events,
    IEventStaffMemberRepository staff,
    IEventUnitOfWork uow,
    ICurrentUserAccessor currentUser,
    IMediator mediator) : IRequestHandler<InviteEventStaffCommand, EventStaffMemberDto>
{
    public async Task<EventStaffMemberDto> Handle(InviteEventStaffCommand request, CancellationToken cancellationToken)
    {
        await ListEventStaffQueryHandler.EnsureOrganizerAsync(events, currentUser, request.EventId, cancellationToken);
        var role = EventStaffMapper.ParseRole(request.Body.Role);
        await StaffStationValidation.ValidateAsync(courses, request.EventId, role, request.Body.StationId, cancellationToken);
        var email = request.Body.Email.Trim().ToLowerInvariant();
        if (await staff.ExistsActiveEmailRoleAsync(request.EventId, role, email, cancellationToken))
        {
            throw new DomainException("Email đã có trong danh sách role này.");
        }

        var race = await events.GetByIdAsync(request.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        var iamRole = role switch
        {
            EventStaffRole.RaceStaff => "Race-staff",
            EventStaffRole.Photographer => "Photographer",
            _ => throw new DomainException("Role nhân sự không hợp lệ.")
        };

        // NotifyEmail = hộp thư nhận SMTP; IAM email/username được gen (không đụng Runner)
        var provisioned = await mediator.Send(
            new RaceDay.Modules.Iam.Application.Users.ProvisionStaff.ProvisionStaffAccountCommand(
                email,
                request.Body.FullName,
                iamRole,
                request.Body.Phone,
                race.Name,
                request.Body.StationLabel),
            cancellationToken);

        // Lưu email liên hệ (SMTP) trên hồ sơ nhân sự — login dùng username gen trong mail
        var member = EventStaffMember.Invite(
            request.EventId,
            role,
            email,
            request.Body.FullName,
            request.Body.Phone,
            request.Body.StationId,
            request.Body.StationLabel);
        member.LinkUser(provisioned.UserId);
        await staff.AddAsync(member, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        return EventStaffMapper.ToDto(member);
    }
}

public sealed class AddEventStaffCommandHandler(
    IRaceCourseReadQuery courses,
    IRaceEventRepository events,
    IEventStaffMemberRepository staff,
    IEventUnitOfWork uow,
    ICurrentUserAccessor currentUser) : IRequestHandler<AddEventStaffCommand, EventStaffMemberDto>
{
    public async Task<EventStaffMemberDto> Handle(AddEventStaffCommand request, CancellationToken cancellationToken)
    {
        await ListEventStaffQueryHandler.EnsureOrganizerAsync(events, currentUser, request.EventId, cancellationToken);
        var role = EventStaffMapper.ParseRole(request.Body.Role);
        await StaffStationValidation.ValidateAsync(courses, request.EventId, role, request.Body.StationId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.Body.Email))
        {
            var email = request.Body.Email.Trim().ToLowerInvariant();
            if (await staff.ExistsActiveEmailRoleAsync(request.EventId, role, email, cancellationToken))
            {
                throw new DomainException("Email đã có trong danh sách role này.");
            }
        }

        var member = EventStaffMember.AddDirect(
            request.EventId,
            role,
            request.Body.FullName,
            request.Body.Email,
            request.Body.Phone,
            request.Body.StationId,
            request.Body.StationLabel);
        await staff.AddAsync(member, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        return EventStaffMapper.ToDto(member);
    }
}

public sealed class AssignEventStaffStationCommandHandler(
    IRaceCourseReadQuery courses,
    IRaceEventRepository events,
    IEventStaffMemberRepository staff,
    IEventUnitOfWork uow,
    ICurrentUserAccessor currentUser) : IRequestHandler<AssignEventStaffStationCommand, EventStaffMemberDto>
{
    public async Task<EventStaffMemberDto> Handle(AssignEventStaffStationCommand request, CancellationToken cancellationToken)
    {
        await ListEventStaffQueryHandler.EnsureOrganizerAsync(events, currentUser, request.EventId, cancellationToken);
        var member = await staff.GetByIdAsync(request.MemberId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy nhân sự.");
        if (member.EventId != request.EventId)
        {
            throw new DomainException("Nhân sự không thuộc giải này.");
        }

        await StaffStationValidation.ValidateAsync(courses, request.EventId, member.Role, request.Body.StationId, cancellationToken);
        member.AssignStation(request.Body.StationId, request.Body.StationLabel);
        staff.Update(member);
        await uow.SaveChangesAsync(cancellationToken);
        return EventStaffMapper.ToDto(member);
    }
}

public sealed class RevokeEventStaffCommandHandler(
    IRaceEventRepository events,
    IEventStaffMemberRepository staff,
    IEventUnitOfWork uow,
    ICurrentUserAccessor currentUser) : IRequestHandler<RevokeEventStaffCommand>
{
    public async Task Handle(RevokeEventStaffCommand request, CancellationToken cancellationToken)
    {
        await ListEventStaffQueryHandler.EnsureOrganizerAsync(events, currentUser, request.EventId, cancellationToken);
        var member = await staff.GetByIdAsync(request.MemberId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy nhân sự.");
        if (member.EventId != request.EventId)
        {
            throw new DomainException("Nhân sự không thuộc giải này.");
        }

        member.Revoke();
        staff.Update(member);
        await uow.SaveChangesAsync(cancellationToken);
    }
}

internal static class StaffStationValidation
{
    public static async Task ValidateAsync(IRaceCourseReadQuery courses, Guid eventId, EventStaffRole role,
        string? stationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(stationId)) return;
        if (stationId.Length > 120) throw new DomainException("Mã vị trí quá dài.");
        var station = stationId.Trim();
        if (station.StartsWith("duty:"))
        {
            var valid = role == EventStaffRole.RaceStaff
                ? station is "duty:ops" or "duty:checkin" or "duty:kit"
                : station is "duty:float" or "duty:static";
            if (!valid) throw new DomainException("Nhiệm vụ không phù hợp với vai trò nhân sự.");
            return;
        }
        var parts = station.Split(':', 3);
        if (parts.Length != 3 || parts[0] != "map" || !Guid.TryParse(parts[1], out var distanceId))
            throw new DomainException("Vị trí phải là checkpoint hoặc nhiệm vụ hợp lệ.");
        var course = await courses.GetAsync(eventId, distanceId, ct);
        var checkpoint = course?.Checkpoints.SingleOrDefault(x => x.Id == parts[2]);
        if (checkpoint is null || (role == EventStaffRole.Photographer && checkpoint.Type == "aid"))
            throw new DomainException("Checkpoint không thuộc giải/cự ly hoặc không phù hợp vai trò.");
    }
}
