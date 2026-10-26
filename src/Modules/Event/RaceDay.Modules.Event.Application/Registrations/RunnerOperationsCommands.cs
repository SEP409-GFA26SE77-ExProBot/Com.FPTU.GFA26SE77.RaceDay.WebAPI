using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Modules.Event.Domain.Staff;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Registrations;

public sealed record UpdateRunnerStatusRequest(string Status, string Reason, long? ExpectedVersion, Guid RequestId);
public sealed record UpdateRunnerStatusCommand(Guid EventId, Guid RegistrationId, string Kind,
    UpdateRunnerStatusRequest Body) : IRequest<RunnerOperationDto>;

public sealed record RunnerOperationDto(Guid Id, Guid RequestId, Guid RegistrationId, string Kind,
    string RequestedStatus, string PreviousStatus, string NewStatus, string Reason, Guid ActorUserId,
    DateTime CreatedAtUtc, long Version, string BibNumber, string RaceStatus, string BibStatus, bool AlreadyApplied);

public sealed record RunnerOperationsDto(Guid RegistrationId, Guid EventId, string FullName, string BibNumber,
    string RegistrationStatus, string RaceStatus, string BibStatus, long Version,
    DateTime? CheckedInAtUtc, DateTime? StartedAtUtc, DateTime? FinishedAtUtc, long? ResultSeconds,
    IReadOnlyList<RunnerOperationDto> Operations);

public sealed class UpdateRunnerStatusCommandValidator : AbstractValidator<UpdateRunnerStatusCommand>
{
    public UpdateRunnerStatusCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.RegistrationId).NotEmpty();
        RuleFor(x => x.Kind).Must(x => x is "race_status" or "bib_status");
        RuleFor(x => x.Body).NotNull();
        When(x => x.Body is not null, () =>
        {
            RuleFor(x => x.Body.RequestId).NotEmpty();
            RuleFor(x => x.Body.ExpectedVersion).NotNull().GreaterThanOrEqualTo(0);
            RuleFor(x => x.Body.Reason).NotEmpty().MaximumLength(1000);
            RuleFor(x => x.Body.Status).NotEmpty().Must((command, status) =>
                command.Kind == "race_status"
                    ? Normalize(status) is "dns" or "dnf" or "dq" or "restore"
                    : Normalize(status) is "lost" or "reissued")
                .WithMessage("race-status: dns|dnf|dq|restore; bib-status: lost|reissued.");
        });
    }

    internal static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}

internal static class RunnerOperationsAccess
{
    public static async Task<(Guid UserId, bool CanCorrect)> EnsureAsync(Guid eventId,
        IRaceEventRepository events, IEventStaffMemberRepository staff, ICurrentUserAccessor currentUser,
        CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenException("Cần đăng nhập.");
        var race = await events.GetByIdAsync(eventId, ct) ?? throw new NotFoundException("Không tìm thấy giải.");
        if (currentUser.IsAdmin || race.OrganizerUserId == userId)
            return (userId, true);

        var memberships = await staff.ListActiveByUserIdAsync(userId, ct);
        var matching = memberships.Where(x => x.EventId == eventId && x.UserId == userId
            && x.Status == EventStaffStatus.Active).ToList();
        if (matching.Any(x => x.Role == EventStaffRole.Coordinator))
            return (userId, true);
        if (matching.Any(x => x.Role == EventStaffRole.Volunteer))
            return (userId, false);
        throw new ForbiddenException("Cần là volunteer/coordinator Active của đúng giải.");
    }

    public static RunnerOperationDto ToDto(RunnerOperation op, bool alreadyApplied = false) => new(
        op.Id, op.RequestId, op.RegistrationId, op.Kind, op.RequestedStatus, op.PreviousStatus, op.NewStatus,
        op.Reason, op.ActorUserId, op.CreatedAtUtc, op.Version, op.BibNumber,
        RegistrationDtoMapper.ToApiRaceStatus(op.RaceStatus), op.BibStatus.ToString().ToLowerInvariant(), alreadyApplied);
}

public sealed class UpdateRunnerStatusCommandHandler(IRaceEventRepository events,
    IEventRegistrationRepository registrations, IEventStaffMemberRepository staff,
    ICurrentUserAccessor currentUser, IEventUnitOfWork unitOfWork)
    : IRequestHandler<UpdateRunnerStatusCommand, RunnerOperationDto>
{
    public async Task<RunnerOperationDto> Handle(UpdateRunnerStatusCommand request, CancellationToken ct)
    {
        var (userId, canCorrect) = await RunnerOperationsAccess.EnsureAsync(request.EventId, events, staff, currentUser, ct);
        var reg = await registrations.GetByIdAsync(request.RegistrationId, ct);
        if (reg is null || reg.EventId != request.EventId)
            throw new NotFoundException("Không tìm thấy đăng ký trong giải này.");

        var status = UpdateRunnerStatusCommandValidator.Normalize(request.Body.Status);
        var reason = request.Body.Reason.Trim();
        var expectedVersion = request.Body.ExpectedVersion
            ?? throw new DomainException("Thiếu expectedVersion.");
        // Check receipt before version: retries of a successful request carry the old version.
        var existing = await registrations.GetOperationAsync(reg.Id, request.Body.RequestId, ct);
        if (existing is not null)
        {
            if (existing.Kind != request.Kind || existing.RequestedStatus != status || existing.Reason != reason
                || existing.ActorUserId != userId || existing.ExpectedVersion != expectedVersion)
                throw new ConflictException("requestId đã dùng với nội dung/người thao tác khác.");
            return RunnerOperationsAccess.ToDto(existing, true);
        }
        if (reg.Version != expectedVersion)
            throw new ConflictException("Version không còn mới. Tải lại dữ liệu trước khi thực hiện thao tác mới.");

        string previous;
        string next;
        if (request.Kind == "race_status")
        {
            previous = RegistrationDtoMapper.ToApiRaceStatus(reg.RaceStatus);
            if (status == "restore")
                reg.RestoreRaceStatus(canCorrect);
            else
                reg.SetRaceStatus(status switch
                {
                    "dns" => RunnerRaceStatus.Dns,
                    "dnf" => RunnerRaceStatus.Dnf,
                    "dq" => RunnerRaceStatus.Dq,
                    _ => throw new DomainException("Trạng thái thi đấu không hợp lệ.")
                }, canCorrect);
            next = RegistrationDtoMapper.ToApiRaceStatus(reg.RaceStatus);
        }
        else if (request.Kind == "bib_status")
        {
            previous = reg.BibStatus.ToString().ToLowerInvariant();
            reg.SetBibStatus(status switch
            {
                "lost" => PhysicalBibStatus.Lost,
                "reissued" => PhysicalBibStatus.Reissued,
                _ => throw new DomainException("Trạng thái BIB không hợp lệ.")
            });
            next = reg.BibStatus.ToString().ToLowerInvariant();
        }
        else throw new DomainException("Loại thao tác không hợp lệ.");

        var operation = RunnerOperation.Create(reg, request.Body.RequestId, request.Kind, status,
            previous, next, reason, userId, expectedVersion);
        registrations.Update(reg);
        await registrations.AddOperationAsync(operation, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return RunnerOperationsAccess.ToDto(operation);
    }
}

public sealed record GetRunnerOperationsQuery(Guid EventId, Guid RegistrationId) : IRequest<RunnerOperationsDto>;

public sealed class GetRunnerOperationsQueryHandler(IRaceEventRepository events,
    IEventRegistrationRepository registrations, IEventStaffMemberRepository staff, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetRunnerOperationsQuery, RunnerOperationsDto>
{
    public async Task<RunnerOperationsDto> Handle(GetRunnerOperationsQuery request, CancellationToken ct)
    {
        await RunnerOperationsAccess.EnsureAsync(request.EventId, events, staff, currentUser, ct);
        var reg = await registrations.GetByIdAsync(request.RegistrationId, ct);
        if (reg is null || reg.EventId != request.EventId)
            throw new NotFoundException("Không tìm thấy đăng ký trong giải này.");
        var operations = await registrations.ListOperationsAsync(reg.Id, ct);
        return new(reg.Id, reg.EventId, reg.FullName, reg.BibNumber ?? string.Empty,
            RegistrationDtoMapper.ToApiStatus(reg.Status), RegistrationDtoMapper.ToApiRaceStatus(reg.RaceStatus),
            reg.BibStatus.ToString().ToLowerInvariant(), reg.Version, reg.CheckedInAtUtc, reg.StartedAtUtc,
            reg.FinishedAtUtc, reg.ResultSeconds, operations.Select(x => RunnerOperationsAccess.ToDto(x)).ToList());
    }
}
