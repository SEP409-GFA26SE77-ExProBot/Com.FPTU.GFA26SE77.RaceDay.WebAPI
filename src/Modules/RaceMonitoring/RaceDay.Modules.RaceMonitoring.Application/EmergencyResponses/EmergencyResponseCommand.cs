using FluentValidation;
using MediatR;
using RaceDay.Modules.Device.Contracts.Sos;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Application.EmergencyResponses;

public sealed record EmergencyResponseDto(
    Guid Id,
    Guid SourceSosAlertId,
    Guid? EventId,
    string SourceSosStatus,
    string IncidentType,
    string Priority,
    string Status,
    DateTime? AssignedAtUtc,
    DateTime? ResolvedAtUtc,
    string? ResolutionNotes,
    DateTime? CancelledAtUtc,
    string? CancellationReason);

public sealed record PagedEmergencyResponsesResult(
    IReadOnlyList<EmergencyResponseDto> Items,
    int Total,
    int Page,
    int PageSize);

public sealed record UpdateEmergencyResponseRequest(string? Priority, string? Status);

public sealed record ResolveEmergencyResponseRequest(string? ResolutionNotes);

public sealed record ListEmergencyResponsesQuery(
    Guid EventId,
    string? Status,
    string? Priority,
    string? IncidentType,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedEmergencyResponsesResult>;

public sealed record GetEmergencyResponseQuery(Guid Id) : IRequest<EmergencyResponseDto>;

public sealed record AcknowledgeEmergencyResponseCommand(Guid Id) : IRequest<EmergencyResponseDto>;

public sealed record UpdateEmergencyResponseCommand(Guid Id, UpdateEmergencyResponseRequest Body)
    : IRequest<EmergencyResponseDto>;

public sealed record ResolveEmergencyResponseCommand(Guid Id, ResolveEmergencyResponseRequest Body)
    : IRequest<EmergencyResponseDto>;

internal static class EmergencyResponseEnumParser
{
    public static bool IsDefinedName<TEnum>(string? value)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return Enum.GetNames<TEnum>().Any(name => name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public static TEnum? ParseOptional<TEnum>(string? value, string message)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!IsDefinedName<TEnum>(value))
        {
            throw new DomainException(message);
        }

        return Enum.Parse<TEnum>(value.Trim(), ignoreCase: true);
    }
}

internal static class EmergencyResponseMapper
{
    public static EmergencyResponseDto ToDto(EmergencyResponse response, SosAlertScope scope) => new(
        response.Id,
        response.SourceSosAlertId,
        scope.EventId,
        scope.Status,
        response.IncidentType.ToString(),
        response.Priority.ToString(),
        response.Status.ToString(),
        response.AssignedAtUtc,
        response.ResolvedAtUtc,
        response.ResolutionNotes,
        response.CancelledAtUtc,
        response.CancellationReason);
}

internal static class EmergencyResponseAccess
{
    public static async Task EnsureCanViewEventAsync(
        Guid eventId,
        IEventOrganizerAccessQuery access,
        IEventSosOpsAccessQuery sosOps,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        _ = await access.GetAsync(eventId, cancellationToken) ?? throw new DomainException("Không tìm thấy giải.");
        if (!currentUser.IsAdmin
            && !await sosOps.CanManageSosAsync(eventId, userId, cancellationToken))
        {
            throw new DomainException("Chỉ organizer của giải hoặc Coordinator Active mới xem phản hồi khẩn cấp.");
        }
    }

    /// <summary>
    /// Reads the source SOS scope. Does not acknowledge or resolve that SOS.
    /// </summary>
    public static async Task<SosAlertScope> EnsureCanAccessAsync(
        EmergencyResponse response,
        bool managing,
        ISosAlertScopeQuery scopes,
        IEventOrganizerAccessQuery access,
        IEventSosOpsAccessQuery sosOps,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var scope = await scopes.GetAsync(response.SourceSosAlertId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy SOS nguồn.");
        if (scope.EventId is null)
        {
            if (!currentUser.IsAdmin)
            {
                throw new DomainException("SOS nguồn chưa gắn giải — chỉ Admin xử lý.");
            }

            return scope;
        }

        _ = await access.GetAsync(scope.EventId.Value, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");
        if (!currentUser.IsAdmin
            && !await sosOps.CanManageSosAsync(scope.EventId.Value, userId, cancellationToken))
        {
            var action = managing ? "xử lý" : "xem";
            throw new DomainException($"Chỉ organizer của giải hoặc Coordinator Active mới {action} phản hồi khẩn cấp.");
        }

        return scope;
    }

    /// <summary>
    /// Chủ giải hoặc Coordinator Active trên giải. Admin không được đi tắt quyền này.
    /// </summary>
    public static async Task EnsureCanCoordinateEventAsync(
        Guid eventId,
        IEventOrganizerAccessQuery access,
        IEventSosOpsAccessQuery sosOps,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        _ = await access.GetAsync(eventId, cancellationToken) ?? throw new DomainException("Không tìm thấy giải.");
        if (!await sosOps.CanManageSosAsync(eventId, userId, cancellationToken))
        {
            throw new DomainException("Chỉ organizer của giải hoặc Coordinator Active mới điều phối nguồn lực.");
        }
    }

    /// <summary>
    /// Đọc phạm vi SOS để biết giải của hồ sơ. Không acknowledge hay resolve SOS nguồn.
    /// </summary>
    public static async Task<SosAlertScope> EnsureCanCoordinateResponseAsync(
        EmergencyResponse response,
        ISosAlertScopeQuery scopes,
        IEventOrganizerAccessQuery access,
        IEventSosOpsAccessQuery sosOps,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var scope = await scopes.GetAsync(response.SourceSosAlertId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy SOS nguồn.");
        if (scope.EventId is null)
        {
            throw new DomainException("Hồ sơ chưa gắn giải nên không điều phối nguồn lực.");
        }

        await EnsureCanCoordinateEventAsync(
            scope.EventId.Value, access, sosOps, currentUser, cancellationToken);
        return scope;
    }
}

public sealed class ListEmergencyResponsesQueryValidator : AbstractValidator<ListEmergencyResponsesQuery>
{
    public ListEmergencyResponsesQueryValidator()
    {
        RuleFor(x => x.EventId).NotEmpty().WithMessage("Tham chiếu sự kiện không hợp lệ.");
    }
}

public sealed class ListEmergencyResponsesQueryHandler(
    IEmergencyResponseRepository responses,
    ISosAlertScopeQuery scopes,
    IEventOrganizerAccessQuery access,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListEmergencyResponsesQuery, PagedEmergencyResponsesResult>
{
    public async Task<PagedEmergencyResponsesResult> Handle(
        ListEmergencyResponsesQuery request,
        CancellationToken cancellationToken)
    {
        await EmergencyResponseAccess.EnsureCanViewEventAsync(
            request.EventId, access, sosOps, currentUser, cancellationToken);

        var status = EmergencyResponseEnumParser.ParseOptional<EmergencyResponseStatus>(
            request.Status, "Trạng thái phản hồi khẩn cấp không hợp lệ.");
        var priority = EmergencyResponseEnumParser.ParseOptional<EmergencyPriority>(
            request.Priority, "Mức ưu tiên khẩn cấp không hợp lệ.");
        var incidentType = EmergencyResponseEnumParser.ParseOptional<EmergencyIncidentType>(
            request.IncidentType, "Loại sự cố khẩn cấp không hợp lệ.");

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? 20 : request.PageSize;
        var sourceScopes = await scopes.ListByEventAsync(request.EventId, cancellationToken);
        if (sourceScopes.Count == 0)
        {
            return new PagedEmergencyResponsesResult([], 0, page, pageSize);
        }

        var scopeById = sourceScopes.ToDictionary(x => x.SosAlertId);
        var (items, total) = await responses.ListBySourceSosIdsAsync(
            scopeById.Keys.ToArray(),
            status,
            priority,
            incidentType,
            page,
            pageSize,
            cancellationToken);
        var dtos = items.Select(item => EmergencyResponseMapper.ToDto(item, scopeById[item.SourceSosAlertId])).ToList();
        return new PagedEmergencyResponsesResult(dtos, total, page, pageSize);
    }
}

public sealed class GetEmergencyResponseQueryValidator : AbstractValidator<GetEmergencyResponseQuery>
{
    public GetEmergencyResponseQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Tham chiếu phản hồi khẩn cấp không hợp lệ.");
    }
}

public sealed class GetEmergencyResponseQueryHandler(
    IEmergencyResponseRepository responses,
    ISosAlertScopeQuery scopes,
    IEventOrganizerAccessQuery access,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetEmergencyResponseQuery, EmergencyResponseDto>
{
    public async Task<EmergencyResponseDto> Handle(
        GetEmergencyResponseQuery request,
        CancellationToken cancellationToken)
    {
        var response = await responses.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new DomainException("Không tìm thấy phản hồi khẩn cấp.");
        var scope = await EmergencyResponseAccess.EnsureCanAccessAsync(
            response, managing: false, scopes, access, sosOps, currentUser, cancellationToken);
        return EmergencyResponseMapper.ToDto(response, scope);
    }
}

public sealed class AcknowledgeEmergencyResponseCommandValidator : AbstractValidator<AcknowledgeEmergencyResponseCommand>
{
    public AcknowledgeEmergencyResponseCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Tham chiếu phản hồi khẩn cấp không hợp lệ.");
    }
}

public sealed class AcknowledgeEmergencyResponseCommandHandler(
    IEmergencyResponseRepository responses,
    IRaceMonitoringUnitOfWork unitOfWork,
    ISosAlertScopeQuery scopes,
    IEventOrganizerAccessQuery access,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser) : IRequestHandler<AcknowledgeEmergencyResponseCommand, EmergencyResponseDto>
{
    public async Task<EmergencyResponseDto> Handle(
        AcknowledgeEmergencyResponseCommand request,
        CancellationToken cancellationToken)
    {
        var response = await responses.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new DomainException("Không tìm thấy phản hồi khẩn cấp.");
        var scope = await EmergencyResponseAccess.EnsureCanAccessAsync(
            response, managing: true, scopes, access, sosOps, currentUser, cancellationToken);
        response.Acknowledge(DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return EmergencyResponseMapper.ToDto(response, scope);
    }
}

public sealed class UpdateEmergencyResponseCommandValidator : AbstractValidator<UpdateEmergencyResponseCommand>
{
    public UpdateEmergencyResponseCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Tham chiếu phản hồi khẩn cấp không hợp lệ.");
        RuleFor(x => x.Body).NotNull().WithMessage("Cần mức ưu tiên hoặc trạng thái tiến độ.");
        When(x => x.Body is not null, () =>
        {
            RuleFor(x => x.Body)
                .Must(body => !string.IsNullOrWhiteSpace(body.Priority) || !string.IsNullOrWhiteSpace(body.Status))
                .WithMessage("Cần mức ưu tiên hoặc trạng thái tiến độ.");
            RuleFor(x => x.Body.Priority)
                .Must(priority => string.IsNullOrWhiteSpace(priority) || EmergencyResponseEnumParser.IsDefinedName<EmergencyPriority>(priority))
                .WithMessage("Mức ưu tiên khẩn cấp không hợp lệ.");
            RuleFor(x => x.Body.Status)
                .Must(status => string.IsNullOrWhiteSpace(status) || IsInProgress(status))
                .WithMessage("Chỉ được chuyển tiến độ sang InProgress.");
        });
    }

    private static bool IsInProgress(string status) =>
        EmergencyResponseEnumParser.IsDefinedName<EmergencyResponseStatus>(status)
        && Enum.Parse<EmergencyResponseStatus>(status, ignoreCase: true) == EmergencyResponseStatus.InProgress;
}

public sealed class UpdateEmergencyResponseCommandHandler(
    IEmergencyResponseRepository responses,
    IRaceMonitoringUnitOfWork unitOfWork,
    ISosAlertScopeQuery scopes,
    IEventOrganizerAccessQuery access,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser) : IRequestHandler<UpdateEmergencyResponseCommand, EmergencyResponseDto>
{
    public async Task<EmergencyResponseDto> Handle(
        UpdateEmergencyResponseCommand request,
        CancellationToken cancellationToken)
    {
        var response = await responses.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new DomainException("Không tìm thấy phản hồi khẩn cấp.");
        var scope = await EmergencyResponseAccess.EnsureCanAccessAsync(
            response, managing: true, scopes, access, sosOps, currentUser, cancellationToken);
        var priority = EmergencyResponseEnumParser.ParseOptional<EmergencyPriority>(
            request.Body.Priority, "Mức ưu tiên khẩn cấp không hợp lệ.");
        var status = EmergencyResponseEnumParser.ParseOptional<EmergencyResponseStatus>(
            request.Body.Status, "Trạng thái phản hồi khẩn cấp không hợp lệ.");
        response.UpdateProgress(priority, status);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return EmergencyResponseMapper.ToDto(response, scope);
    }
}

public sealed class ResolveEmergencyResponseCommandValidator : AbstractValidator<ResolveEmergencyResponseCommand>
{
    public ResolveEmergencyResponseCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Tham chiếu phản hồi khẩn cấp không hợp lệ.");
        RuleFor(x => x.Body).NotNull().WithMessage("Nội dung kết thúc phản hồi không được để trống.");
        When(x => x.Body is not null, () =>
        {
            RuleFor(x => x.Body.ResolutionNotes)
                .NotEmpty()
                .WithMessage("Nội dung kết thúc phản hồi không được để trống.");
        });
    }
}

public sealed class ResolveEmergencyResponseCommandHandler(
    IEmergencyResponseRepository responses,
    IRaceMonitoringUnitOfWork unitOfWork,
    ISosAlertScopeQuery scopes,
    IEventOrganizerAccessQuery access,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser) : IRequestHandler<ResolveEmergencyResponseCommand, EmergencyResponseDto>
{
    public async Task<EmergencyResponseDto> Handle(
        ResolveEmergencyResponseCommand request,
        CancellationToken cancellationToken)
    {
        var response = await responses.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new DomainException("Không tìm thấy phản hồi khẩn cấp.");
        var scope = await EmergencyResponseAccess.EnsureCanAccessAsync(
            response, managing: true, scopes, access, sosOps, currentUser, cancellationToken);
        response.Resolve(request.Body.ResolutionNotes, DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return EmergencyResponseMapper.ToDto(response, scope);
    }
}
