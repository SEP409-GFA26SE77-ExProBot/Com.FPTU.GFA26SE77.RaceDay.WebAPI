using FluentValidation;
using MediatR;
using RaceDay.Modules.Device.Contracts.Sos;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Application.EmergencyResponses;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Application.EmergencyAssignments;

public sealed record EmergencyAssignmentDto(
    Guid Id,
    Guid EmergencyResponseId,
    Guid EmergencyResourceId,
    string ResourceCode,
    string ResourceName,
    string ResourceType,
    string ResourceStatus,
    DateTime AssignedAtUtc,
    DateTime? AcceptedAtUtc,
    DateTime? ArrivedAtUtc,
    DateTime? CompletedAtUtc,
    string Status,
    string? Notes);

public sealed record PagedEmergencyAssignmentsResult(
    IReadOnlyList<EmergencyAssignmentDto> Items,
    int Total,
    int Page,
    int PageSize);

public sealed record CreateEmergencyAssignmentRequest(Guid EmergencyResourceId, string? Notes);

public sealed record UpdateEmergencyAssignmentRequest(string? Status);

public sealed record ListEmergencyAssignmentsQuery(
    Guid ResponseId,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedEmergencyAssignmentsResult>;

public sealed record GetEmergencyAssignmentQuery(Guid Id) : IRequest<EmergencyAssignmentDto>;

public sealed record CreateEmergencyAssignmentCommand(Guid ResponseId, CreateEmergencyAssignmentRequest Body)
    : IRequest<EmergencyAssignmentDto>;

public sealed record UpdateEmergencyAssignmentCommand(Guid Id, UpdateEmergencyAssignmentRequest Body)
    : IRequest<EmergencyAssignmentDto>;

public sealed record RemoveEmergencyAssignmentCommand(Guid ResponseId, Guid AssignmentId) : IRequest;

internal static class EmergencyAssignmentMapper
{
    public static EmergencyAssignmentDto ToDto(EmergencyAssignment assignment, EmergencyResource resource) => new(
        assignment.Id,
        assignment.EmergencyResponseId,
        assignment.EmergencyResourceId,
        resource.ResourceCode,
        resource.Name,
        resource.ResourceType.ToString(),
        resource.Status.ToString(),
        assignment.AssignedAtUtc,
        assignment.AcceptedAtUtc,
        assignment.ArrivedAtUtc,
        assignment.CompletedAtUtc,
        assignment.Status.ToString(),
        assignment.Notes);

    public static EmergencyAssignmentDto ToDto(EmergencyAssignment assignment)
    {
        var resource = assignment.EmergencyResource
            ?? throw new DomainException("Không tìm thấy nguồn lực.");
        return ToDto(assignment, resource);
    }
}

internal static class EmergencyAssignmentRules
{
    public static void EnsureResourceCanBeAssigned(EmergencyResource resource, Guid eventId)
    {
        if (resource.EventId != eventId)
        {
            throw new DomainException("Nguồn lực không thuộc giải của hồ sơ ứng cứu.");
        }

        if (resource.Status is EmergencyResourceStatus.Unavailable or EmergencyResourceStatus.Offline)
        {
            throw new DomainException("Nguồn lực không khả dụng để phân công.");
        }
    }

    public static EmergencyAssignmentStatus ParseProgressStatus(string? status)
    {
        var parsed = EmergencyResponseEnumParser.ParseOptional<EmergencyAssignmentStatus>(
            status, "Trạng thái phân công không hợp lệ.");
        if (parsed is not (EmergencyAssignmentStatus.Accepted or EmergencyAssignmentStatus.Arrived or EmergencyAssignmentStatus.Completed))
        {
            throw new DomainException("Chỉ được chuyển phân công sang Accepted, Arrived hoặc Completed.");
        }

        return parsed.Value;
    }
}

public sealed class ListEmergencyAssignmentsQueryValidator : AbstractValidator<ListEmergencyAssignmentsQuery>
{
    public ListEmergencyAssignmentsQueryValidator()
    {
        RuleFor(x => x.ResponseId).NotEmpty().WithMessage("Tham chiếu phản hồi khẩn cấp không hợp lệ.");
    }
}

public sealed class ListEmergencyAssignmentsQueryHandler(
    IEmergencyResponseRepository responses,
    IEmergencyAssignmentRepository assignments,
    ISosAlertScopeQuery scopes,
    IEventOrganizerAccessQuery access,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListEmergencyAssignmentsQuery, PagedEmergencyAssignmentsResult>
{
    public async Task<PagedEmergencyAssignmentsResult> Handle(
        ListEmergencyAssignmentsQuery request,
        CancellationToken cancellationToken)
    {
        var response = await responses.GetByIdAsync(request.ResponseId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy phản hồi khẩn cấp.");
        await EmergencyResponseAccess.EnsureCanCoordinateResponseAsync(
            response, scopes, access, sosOps, currentUser, cancellationToken);
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? 20 : request.PageSize;
        var (items, total) = await assignments.ListByResponseAsync(
            response.Id, page, pageSize, cancellationToken);
        return new PagedEmergencyAssignmentsResult(
            items.Select(EmergencyAssignmentMapper.ToDto).ToList(),
            total,
            page,
            pageSize);
    }
}

public sealed class GetEmergencyAssignmentQueryValidator : AbstractValidator<GetEmergencyAssignmentQuery>
{
    public GetEmergencyAssignmentQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Tham chiếu phân công không hợp lệ.");
    }
}

public sealed class GetEmergencyAssignmentQueryHandler(
    IEmergencyResponseRepository responses,
    IEmergencyAssignmentRepository assignments,
    ISosAlertScopeQuery scopes,
    IEventOrganizerAccessQuery access,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetEmergencyAssignmentQuery, EmergencyAssignmentDto>
{
    public async Task<EmergencyAssignmentDto> Handle(
        GetEmergencyAssignmentQuery request,
        CancellationToken cancellationToken)
    {
        var assignment = await assignments.GetWithResourceAsync(request.Id, cancellationToken)
            ?? throw new DomainException("Không tìm thấy phân công.");
        var response = await responses.GetByIdAsync(assignment.EmergencyResponseId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy phản hồi khẩn cấp.");
        await EmergencyResponseAccess.EnsureCanCoordinateResponseAsync(
            response, scopes, access, sosOps, currentUser, cancellationToken);
        return EmergencyAssignmentMapper.ToDto(assignment);
    }
}

public sealed class CreateEmergencyAssignmentCommandValidator : AbstractValidator<CreateEmergencyAssignmentCommand>
{
    public CreateEmergencyAssignmentCommandValidator()
    {
        RuleFor(x => x.ResponseId).NotEmpty().WithMessage("Tham chiếu phản hồi khẩn cấp không hợp lệ.");
        RuleFor(x => x.Body).NotNull().WithMessage("Cần nguồn lực để phân công.");
        When(x => x.Body is not null, () =>
        {
            RuleFor(x => x.Body.EmergencyResourceId).NotEmpty().WithMessage("Tham chiếu nguồn lực không hợp lệ.");
        });
    }
}

public sealed class CreateEmergencyAssignmentCommandHandler(
    IEmergencyResponseRepository responses,
    IEmergencyResourceRepository resources,
    IEmergencyAssignmentRepository assignments,
    IRaceMonitoringUnitOfWork unitOfWork,
    ISosAlertScopeQuery scopes,
    IEventOrganizerAccessQuery access,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser) : IRequestHandler<CreateEmergencyAssignmentCommand, EmergencyAssignmentDto>
{
    public async Task<EmergencyAssignmentDto> Handle(
        CreateEmergencyAssignmentCommand request,
        CancellationToken cancellationToken)
    {
        var response = await responses.GetByIdAsync(request.ResponseId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy phản hồi khẩn cấp.");
        var scope = await EmergencyResponseAccess.EnsureCanCoordinateResponseAsync(
            response, scopes, access, sosOps, currentUser, cancellationToken);
        if (response.Status != EmergencyResponseStatus.Open)
        {
            throw new DomainException("Chỉ hồ sơ đang mở mới được phân công nguồn lực.");
        }

        var resource = await resources.GetByIdAsync(request.Body.EmergencyResourceId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy nguồn lực.");
        var eventId = scope.EventId
            ?? throw new DomainException("Hồ sơ chưa gắn giải nên không điều phối nguồn lực.");
        EmergencyAssignmentRules.EnsureResourceCanBeAssigned(resource, eventId);
        if (await assignments.ExistsForResponseAsync(response.Id, resource.Id, cancellationToken))
        {
            throw new DomainException("Nguồn lực này đã nằm trong danh sách phân công của hồ sơ.");
        }

        var assignment = EmergencyAssignment.Create(
            response.Id,
            resource.Id,
            DateTime.UtcNow,
            EmergencyAssignmentStatus.Assigned,
            notes: request.Body.Notes);
        assignments.Add(assignment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return EmergencyAssignmentMapper.ToDto(assignment, resource);
    }
}

public sealed class UpdateEmergencyAssignmentCommandValidator : AbstractValidator<UpdateEmergencyAssignmentCommand>
{
    public UpdateEmergencyAssignmentCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Tham chiếu phân công không hợp lệ.");
        RuleFor(x => x.Body).NotNull().WithMessage("Cần trạng thái phân công.");
        When(x => x.Body is not null, () =>
        {
            RuleFor(x => x.Body.Status)
                .Must(status => !string.IsNullOrWhiteSpace(status) && IsProgressStatus(status))
                .WithMessage("Chỉ được chuyển phân công sang Accepted, Arrived hoặc Completed.");
        });
    }

    private static bool IsProgressStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)
            || !EmergencyResponseEnumParser.IsDefinedName<EmergencyAssignmentStatus>(status))
        {
            return false;
        }

        var parsed = Enum.Parse<EmergencyAssignmentStatus>(status, ignoreCase: true);
        return parsed is EmergencyAssignmentStatus.Accepted
            or EmergencyAssignmentStatus.Arrived
            or EmergencyAssignmentStatus.Completed;
    }
}

public sealed class UpdateEmergencyAssignmentCommandHandler(
    IEmergencyResponseRepository responses,
    IEmergencyResourceRepository resources,
    IEmergencyAssignmentRepository assignments,
    IRaceMonitoringUnitOfWork unitOfWork,
    ISosAlertScopeQuery scopes,
    IEventOrganizerAccessQuery access,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser) : IRequestHandler<UpdateEmergencyAssignmentCommand, EmergencyAssignmentDto>
{
    public async Task<EmergencyAssignmentDto> Handle(
        UpdateEmergencyAssignmentCommand request,
        CancellationToken cancellationToken)
    {
        var assignment = await assignments.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new DomainException("Không tìm thấy phân công.");
        var response = await responses.GetByIdAsync(assignment.EmergencyResponseId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy phản hồi khẩn cấp.");
        await EmergencyResponseAccess.EnsureCanCoordinateResponseAsync(
            response, scopes, access, sosOps, currentUser, cancellationToken);
        if (response.Status is EmergencyResponseStatus.Resolved or EmergencyResponseStatus.Cancelled)
        {
            throw new DomainException("Không thể cập nhật phân công khi hồ sơ đã kết thúc.");
        }

        var resource = await resources.GetByIdAsync(assignment.EmergencyResourceId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy nguồn lực.");
        var nextStatus = EmergencyAssignmentRules.ParseProgressStatus(request.Body.Status);
        assignment.Advance(nextStatus, DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return EmergencyAssignmentMapper.ToDto(assignment, resource);
    }
}

public sealed class RemoveEmergencyAssignmentCommandValidator : AbstractValidator<RemoveEmergencyAssignmentCommand>
{
    public RemoveEmergencyAssignmentCommandValidator()
    {
        RuleFor(x => x.ResponseId).NotEmpty().WithMessage("Tham chiếu phản hồi khẩn cấp không hợp lệ.");
        RuleFor(x => x.AssignmentId).NotEmpty().WithMessage("Tham chiếu phân công không hợp lệ.");
    }
}

public sealed class RemoveEmergencyAssignmentCommandHandler(
    IEmergencyResponseRepository responses,
    IEmergencyAssignmentRepository assignments,
    IRaceMonitoringUnitOfWork unitOfWork,
    ISosAlertScopeQuery scopes,
    IEventOrganizerAccessQuery access,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser) : IRequestHandler<RemoveEmergencyAssignmentCommand>
{
    public async Task Handle(RemoveEmergencyAssignmentCommand request, CancellationToken cancellationToken)
    {
        var response = await responses.GetByIdAsync(request.ResponseId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy phản hồi khẩn cấp.");
        await EmergencyResponseAccess.EnsureCanCoordinateResponseAsync(
            response, scopes, access, sosOps, currentUser, cancellationToken);
        var assignment = await assignments.GetByIdAsync(request.AssignmentId, cancellationToken);
        if (assignment is null || assignment.EmergencyResponseId != response.Id)
        {
            throw new DomainException("Không tìm thấy phân công.");
        }

        if (response.Status != EmergencyResponseStatus.Open)
        {
            throw new DomainException("Chỉ hồ sơ đang mở mới gỡ được phân công.");
        }

        assignments.Remove(assignment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
