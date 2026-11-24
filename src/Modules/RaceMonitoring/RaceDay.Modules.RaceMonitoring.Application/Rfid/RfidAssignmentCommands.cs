using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Application.TimingReads;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Application.Rfid;

public sealed record RfidAssignmentDto(
    Guid AssignmentId,
    Guid EventId,
    Guid RegistrationId,
    string TagScheme,
    string TagId,
    Guid AssignedByUserId,
    DateTime AssignedAtUtc,
    bool Created);

public sealed record RfidReaderDto(string ReaderId, Guid EventId, Guid CheckpointId, string Mode);

public sealed record CreateRfidReadRequestCommand(Guid EventId, Guid RegistrationId, string? ReaderId)
    : IRequest<RfidReadRequestDto>;

public sealed class CreateRfidReadRequestCommandValidator : AbstractValidator<CreateRfidReadRequestCommand>
{
    public CreateRfidReadRequestCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty().WithMessage("Tham chiếu giải không hợp lệ.");
        RuleFor(x => x.RegistrationId).NotEmpty().WithMessage("Tham chiếu đăng ký không hợp lệ.");
        RuleFor(x => x.ReaderId).NotEmpty().WithMessage("Mã đầu đọc không hợp lệ.");
    }
}

public sealed class CreateRfidReadRequestCommandHandler(
    RfidReadRequestBoard board,
    RfidGatewayOptions options,
    IEventOrganizerAccessQuery access,
    IEventRegistrationRaceQuery registrations,
    ICurrentUserAccessor currentUser)
    : IRequestHandler<CreateRfidReadRequestCommand, RfidReadRequestDto>
{
    public async Task<RfidReadRequestDto> Handle(
        CreateRfidReadRequestCommand request,
        CancellationToken cancellationToken)
    {
        var userId = await RfidOrganizerAccess.RequireOwnerAsync(
            request.EventId, access, currentUser, cancellationToken);
        var registration = await registrations.GetByIdAsync(request.RegistrationId, cancellationToken);
        RfidRegistrationRules.EnsureAssignable(registration, request.EventId);
        RfidReaderCatalog.Require(options, request.ReaderId!, request.EventId);
        return board.Create(request.EventId, request.RegistrationId, request.ReaderId!, userId);
    }
}

public sealed record GetRfidReadRequestQuery(Guid RequestId) : IRequest<RfidReadRequestDto>;

public sealed class GetRfidReadRequestQueryHandler(
    RfidReadRequestBoard board,
    IEventOrganizerAccessQuery access,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetRfidReadRequestQuery, RfidReadRequestDto>
{
    public async Task<RfidReadRequestDto> Handle(GetRfidReadRequestQuery request, CancellationToken cancellationToken)
    {
        var found = board.Find(request.RequestId) ?? throw new DomainException("Không tìm thấy yêu cầu đọc.");
        await RfidOrganizerAccess.RequireOwnerAsync(found.EventId, access, currentUser, cancellationToken);
        return found;
    }
}

public sealed record CancelRfidReadRequestCommand(Guid RequestId) : IRequest<RfidReadRequestDto>;

public sealed class CancelRfidReadRequestCommandHandler(
    RfidReadRequestBoard board,
    IEventOrganizerAccessQuery access,
    ICurrentUserAccessor currentUser) : IRequestHandler<CancelRfidReadRequestCommand, RfidReadRequestDto>
{
    public async Task<RfidReadRequestDto> Handle(
        CancelRfidReadRequestCommand request,
        CancellationToken cancellationToken)
    {
        var found = board.Find(request.RequestId) ?? throw new DomainException("Không tìm thấy yêu cầu đọc.");
        var userId = await RfidOrganizerAccess.RequireOwnerAsync(
            found.EventId, access, currentUser, cancellationToken);
        return board.Cancel(request.RequestId, userId);
    }
}

public sealed record RfidObservedEpc(string? TagId, DateTime ObservedAtUtc);

public sealed record SubmitRfidObservationsCommand(
    Guid RequestId,
    string? ReaderId,
    IReadOnlyList<RfidObservedEpc>? Epcs) : IRequest<RfidReadRequestDto>;

public sealed class SubmitRfidObservationsCommandValidator : AbstractValidator<SubmitRfidObservationsCommand>
{
    public SubmitRfidObservationsCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty().WithMessage("Mã yêu cầu đọc không hợp lệ.");
        RuleFor(x => x.ReaderId).NotEmpty().WithMessage("Mã đầu đọc không hợp lệ.");
        RuleForEach(x => x.Epcs).ChildRules(epc =>
        {
            epc.RuleFor(x => x.TagId).NotEmpty().WithMessage("Mã thẻ không hợp lệ.");
            epc.RuleFor(x => x.ObservedAtUtc)
                .Must(value => value != default)
                .WithMessage("Thời điểm quan sát không hợp lệ.");
        });
    }
}

/// <summary>Gateway nộp EPC của lượt gán. Không ghi checkpoint và không tạo timeline.</summary>
public sealed class SubmitRfidObservationsCommandHandler(RfidReadRequestBoard board)
    : IRequestHandler<SubmitRfidObservationsCommand, RfidReadRequestDto>
{
    public Task<RfidReadRequestDto> Handle(
        SubmitRfidObservationsCommand request,
        CancellationToken cancellationToken)
    {
        var tags = (request.Epcs ?? [])
            .Select(epc => TimingTagIdentity.Normalize("EPC", epc.TagId))
            .ToArray();
        return Task.FromResult(board.Observe(request.RequestId, request.ReaderId!, tags));
    }
}

public sealed record ClaimNextRfidReadRequestQuery(string? ReaderId, Guid? RequestId) : IRequest<RfidGatewayPollDto>;

public sealed class ClaimNextRfidReadRequestQueryValidator : AbstractValidator<ClaimNextRfidReadRequestQuery>
{
    public ClaimNextRfidReadRequestQueryValidator()
    {
        RuleFor(x => x.ReaderId).NotEmpty().WithMessage("Mã đầu đọc không hợp lệ.");
    }
}

public sealed class ClaimNextRfidReadRequestQueryHandler(RfidReadRequestBoard board)
    : IRequestHandler<ClaimNextRfidReadRequestQuery, RfidGatewayPollDto>
{
    public Task<RfidGatewayPollDto> Handle(
        ClaimNextRfidReadRequestQuery request,
        CancellationToken cancellationToken)
        => Task.FromResult(board.ClaimNext(request.ReaderId!, request.RequestId));
}

public sealed record ConfirmRfidAssignmentCommand(Guid RequestId, string? TagId) : IRequest<RfidAssignmentDto>;

public sealed class ConfirmRfidAssignmentCommandValidator : AbstractValidator<ConfirmRfidAssignmentCommand>
{
    public ConfirmRfidAssignmentCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty().WithMessage("Mã yêu cầu đọc không hợp lệ.");
        RuleFor(x => x.TagId).NotEmpty().WithMessage("Mã thẻ không hợp lệ.");
    }
}

public sealed class ConfirmRfidAssignmentCommandHandler(
    RfidReadRequestBoard board,
    RfidGatewayOptions options,
    IRfidTagAssignmentRepository assignments,
    IRaceMonitoringUnitOfWork unitOfWork,
    IEventOrganizerAccessQuery access,
    IEventRegistrationRaceQuery registrations,
    ICurrentUserAccessor currentUser) : IRequestHandler<ConfirmRfidAssignmentCommand, RfidAssignmentDto>
{
    public async Task<RfidAssignmentDto> Handle(
        ConfirmRfidAssignmentCommand request,
        CancellationToken cancellationToken)
    {
        var normalized = TimingTagIdentity.Normalize("EPC", request.TagId);
        var snapshot = board.Find(request.RequestId) ?? throw new DomainException("Không tìm thấy yêu cầu đọc.");
        var userId = await RfidOrganizerAccess.RequireOwnerAsync(
            snapshot.EventId, access, currentUser, cancellationToken);
        board.EnsureConfirmable(request.RequestId, normalized);
        var registration = await registrations.GetByIdAsync(snapshot.RegistrationId, cancellationToken);
        RfidRegistrationRules.EnsureAssignable(registration, snapshot.EventId);
        RfidReaderCatalog.Require(options, snapshot.ReaderId, snapshot.EventId);

        var byTag = await assignments.FindByTagAsync(
            snapshot.EventId, RfidTagAssignment.EpcScheme, normalized, cancellationToken);
        var byRegistration = await assignments.FindByRegistrationAsync(
            snapshot.EventId, snapshot.RegistrationId, cancellationToken);
        if (IsSamePair(byTag, snapshot.RegistrationId, normalized)
            || IsSamePair(byRegistration, snapshot.RegistrationId, normalized))
        {
            board.ClaimConfirm(request.RequestId, normalized);
            return ToDto((byTag ?? byRegistration)!, created: false);
        }

        if (byTag is not null)
        {
            throw new RfidConflictException("EPC đã được gán cho đăng ký khác trong giải này.");
        }

        if (byRegistration is not null)
        {
            throw new RfidConflictException("Đăng ký đã được gán một EPC khác trong giải này.");
        }

        RfidConfigAssignmentGuard.EnsureNoConflict(options, snapshot.EventId, snapshot.RegistrationId, normalized);
        var claim = board.ClaimConfirm(request.RequestId, normalized);
        var created = RfidTagAssignment.Create(
            snapshot.EventId,
            snapshot.RegistrationId,
            normalized,
            userId,
            DateTime.UtcNow);
        assignments.Add(created);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (RfidConflictException)
        {
            var savedTag = await assignments.FindByTagAsync(
                snapshot.EventId, RfidTagAssignment.EpcScheme, normalized, cancellationToken);
            if (IsSamePair(savedTag, snapshot.RegistrationId, normalized))
            {
                return ToDto(savedTag!, created: false);
            }

            if (claim == RfidConfirmClaim.NewlyConfirmed)
            {
                board.RevertConfirm(request.RequestId);
            }

            var savedRegistration = await assignments.FindByRegistrationAsync(
                snapshot.EventId, snapshot.RegistrationId, cancellationToken);
            throw new RfidConflictException(
                savedTag is not null || savedRegistration is null
                    ? "EPC đã được gán cho đăng ký khác trong giải này."
                    : "Đăng ký đã được gán một EPC khác trong giải này.");
        }
        catch
        {
            if (claim == RfidConfirmClaim.NewlyConfirmed)
            {
                board.RevertConfirm(request.RequestId);
            }

            throw;
        }

        return ToDto(created, created: true);
    }

    private static bool IsSamePair(RfidTagAssignment? row, Guid registrationId, string tagId) =>
        row is not null
        && row.RegistrationId == registrationId
        && string.Equals(row.TagId, tagId, StringComparison.OrdinalIgnoreCase);

    private static RfidAssignmentDto ToDto(RfidTagAssignment row, bool created) =>
        new(
            row.Id,
            row.EventId,
            row.RegistrationId,
            row.TagScheme,
            row.TagId,
            row.AssignedByUserId,
            row.AssignedAtUtc,
            created);
}

public sealed record GetRfidAssignmentQuery(Guid EventId, Guid RegistrationId) : IRequest<RfidAssignmentDto?>;

public sealed class GetRfidAssignmentQueryHandler(
    IRfidTagAssignmentRepository assignments,
    IEventOrganizerAccessQuery access,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetRfidAssignmentQuery, RfidAssignmentDto?>
{
    public async Task<RfidAssignmentDto?> Handle(GetRfidAssignmentQuery request, CancellationToken cancellationToken)
    {
        await RfidOrganizerAccess.RequireOwnerAsync(request.EventId, access, currentUser, cancellationToken);
        var row = await assignments.FindByRegistrationAsync(request.EventId, request.RegistrationId, cancellationToken);
        return row is null
            ? null
            : new RfidAssignmentDto(
                row.Id,
                row.EventId,
                row.RegistrationId,
                row.TagScheme,
                row.TagId,
                row.AssignedByUserId,
                row.AssignedAtUtc,
                false);
    }
}

public sealed record ListRfidReadersQuery(Guid EventId) : IRequest<IReadOnlyList<RfidReaderDto>>;

public sealed class ListRfidReadersQueryHandler(
    RfidReadRequestBoard board,
    RfidGatewayOptions options,
    IEventOrganizerAccessQuery access,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListRfidReadersQuery, IReadOnlyList<RfidReaderDto>>
{
    public async Task<IReadOnlyList<RfidReaderDto>> Handle(
        ListRfidReadersQuery request,
        CancellationToken cancellationToken)
    {
        await RfidOrganizerAccess.RequireOwnerAsync(request.EventId, access, currentUser, cancellationToken);
        return RfidReaderCatalog.ForEvent(options, request.EventId)
            .Select(reader => new RfidReaderDto(
                reader.ReaderId.Trim(),
                reader.EventId,
                reader.CheckpointId,
                board.ModeOf(reader.ReaderId)))
            .ToArray();
    }
}

public sealed record StartRfidCheckpointScanCommand(string? ReaderId) : IRequest<RfidReaderModeDto>;

public sealed class StartRfidCheckpointScanCommandValidator : AbstractValidator<StartRfidCheckpointScanCommand>
{
    public StartRfidCheckpointScanCommandValidator()
    {
        RuleFor(x => x.ReaderId).NotEmpty().WithMessage("Mã đầu đọc không hợp lệ.");
    }
}

public sealed class StartRfidCheckpointScanCommandHandler(
    RfidReadRequestBoard board,
    RfidGatewayOptions options,
    IEventOrganizerAccessQuery access,
    ICurrentUserAccessor currentUser) : IRequestHandler<StartRfidCheckpointScanCommand, RfidReaderModeDto>
{
    public async Task<RfidReaderModeDto> Handle(
        StartRfidCheckpointScanCommand request,
        CancellationToken cancellationToken)
    {
        var reader = RfidReaderCatalog.RequireSingle(options, request.ReaderId!);
        await RfidOrganizerAccess.RequireOwnerAsync(reader.EventId, access, currentUser, cancellationToken);
        board.StartCheckpointScan(reader.ReaderId);
        return new RfidReaderModeDto(reader.ReaderId.Trim(), reader.EventId, board.ModeOf(reader.ReaderId));
    }
}

internal static class RfidOrganizerAccess
{
    public static async Task<Guid> RequireOwnerAsync(
        Guid eventId,
        IEventOrganizerAccessQuery access,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var info = await access.GetAsync(eventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");
        if (!currentUser.IsAdmin && info.OrganizerUserId != userId)
        {
            throw new DomainException("Bạn không phải chủ giải này.");
        }

        return userId;
    }
}

internal static class RfidRegistrationRules
{
    public static void EnsureAssignable(EventRegistrationRaceInfo? registration, Guid eventId)
    {
        if (registration is null || registration.EventId != eventId)
        {
            throw new DomainException("Đăng ký không thuộc giải này.");
        }

        if (!string.Equals(registration.Status, "Confirmed", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("Chỉ gán thẻ cho đăng ký Confirmed.");
        }

        if (string.IsNullOrWhiteSpace(registration.BibNumber))
        {
            throw new DomainException("Đăng ký chưa có số BIB.");
        }
    }
}

internal static class RfidReaderCatalog
{
    public static RfidReaderBinding Require(RfidGatewayOptions options, string readerId, Guid eventId)
    {
        var key = readerId.Trim();
        var matches = options.Readers
            .Where(reader =>
                string.Equals(reader.ReaderId?.Trim(), key, StringComparison.OrdinalIgnoreCase)
                && reader.EventId == eventId
                && reader.CheckpointId != Guid.Empty)
            .ToList();
        if (matches.Count != 1)
        {
            throw new DomainException(
                matches.Count > 1
                    ? "Đầu đọc được gán nhiều hơn một checkpoint."
                    : "Đầu đọc chưa được gán cho giải này.");
        }

        return matches[0];
    }

    public static RfidReaderBinding RequireSingle(RfidGatewayOptions options, string readerId)
    {
        var key = readerId.Trim();
        var matches = options.Readers
            .Where(reader =>
                string.Equals(reader.ReaderId?.Trim(), key, StringComparison.OrdinalIgnoreCase)
                && reader.EventId != Guid.Empty
                && reader.CheckpointId != Guid.Empty)
            .ToList();
        if (matches.Count != 1)
        {
            throw new DomainException(
                matches.Count > 1
                    ? "Đầu đọc được gán nhiều hơn một checkpoint."
                    : "Đầu đọc chưa được gán checkpoint.");
        }

        return matches[0];
    }

    public static IReadOnlyList<RfidReaderBinding> ForEvent(RfidGatewayOptions options, Guid eventId) =>
        options.Readers
            .Where(reader =>
                reader.EventId == eventId
                && reader.CheckpointId != Guid.Empty
                && !string.IsNullOrWhiteSpace(reader.ReaderId))
            .ToArray();
}

internal static class RfidConfigAssignmentGuard
{
    public static void EnsureNoConflict(
        RfidGatewayOptions options,
        Guid eventId,
        Guid registrationId,
        string normalizedTag)
    {
        foreach (var tag in options.Tags)
        {
            if (tag.EventId != eventId || !TryNormalize(tag, out var configuredTag))
            {
                continue;
            }

            var sameTag = string.Equals(configuredTag, normalizedTag, StringComparison.OrdinalIgnoreCase);
            var sameRegistration = tag.RegistrationId == registrationId;
            if (sameTag && !sameRegistration)
            {
                throw new RfidConflictException(
                    "EPC này đã được gán cho vận động viên khác trong cấu hình đầu đọc.");
            }

            if (!sameTag && sameRegistration)
            {
                throw new RfidConflictException(
                    "Đăng ký này đã được gán EPC khác trong cấu hình đầu đọc.");
            }
        }
    }

    private static bool TryNormalize(RfidTagBinding tag, out string normalizedTag)
    {
        normalizedTag = "";
        try
        {
            if (!string.Equals(tag.TagScheme?.Trim(), RfidTagAssignment.EpcScheme, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            normalizedTag = TimingTagIdentity.Normalize(tag.TagScheme, tag.TagId);
            return tag.RegistrationId != Guid.Empty;
        }
        catch (DomainException)
        {
            return false;
        }
    }
}
