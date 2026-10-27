using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Application.RaceEventLogs;

public sealed record RaceEventLogDto(
    Guid Id,
    long Sequence,
    Guid EventId,
    Guid? EventDistanceId,
    string EventType,
    DateTime OccurredAtUtc,
    DateTime RecordedAtUtc,
    string? BibNumber,
    Guid? CheckpointId,
    Guid? RegistrationId,
    string? SourceType,
    Guid? SourceId,
    string? ScanMethod);

public sealed record PagedRaceEventLogsResult(
    IReadOnlyList<RaceEventLogDto> Items,
    int Total,
    int Page,
    int PageSize);

public sealed record ListRaceEventLogsQuery(
    Guid EventId,
    Guid? EventDistanceId,
    string? EventType,
    string? BibNumber,
    DateTime? FromUtc,
    DateTime? ToUtc,
    long? AfterSequence,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedRaceEventLogsResult>;

public sealed record GetRaceEventLogQuery(Guid EventId, Guid LogId) : IRequest<RaceEventLogDto>;

public sealed record EnsureRaceTimelineAccessQuery(Guid EventId) : IRequest;

public sealed class ListRaceEventLogsQueryValidator : AbstractValidator<ListRaceEventLogsQuery>
{
    public ListRaceEventLogsQueryValidator()
    {
        RuleFor(x => x.EventId).NotEmpty().WithMessage("Tham chiếu giải không hợp lệ.");
    }
}

public sealed class GetRaceEventLogQueryValidator : AbstractValidator<GetRaceEventLogQuery>
{
    public GetRaceEventLogQueryValidator()
    {
        RuleFor(x => x.EventId).NotEmpty().WithMessage("Tham chiếu giải không hợp lệ.");
        RuleFor(x => x.LogId).NotEmpty().WithMessage("Tham chiếu log không hợp lệ.");
    }
}

public sealed class ListRaceEventLogsQueryHandler(
    IRaceEventLogRepository logs,
    IEventOrganizerAccessQuery access,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListRaceEventLogsQuery, PagedRaceEventLogsResult>
{
    public async Task<PagedRaceEventLogsResult> Handle(
        ListRaceEventLogsQuery request,
        CancellationToken cancellationToken)
    {
        await RaceTimelineAccess.EnsureCanViewAsync(
            request.EventId, access, sosOps, currentUser, cancellationToken);

        var eventType = ParseEventType(request.EventType);
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? 20 : request.PageSize;
        var (items, total) = await logs.ListAsync(
            new RaceEventLogFilter(
                request.EventId,
                request.EventDistanceId,
                eventType,
                string.IsNullOrWhiteSpace(request.BibNumber) ? null : request.BibNumber.Trim(),
                AsUtc(request.FromUtc),
                AsUtc(request.ToUtc),
                request.AfterSequence,
                page,
                pageSize),
            cancellationToken);
        return new PagedRaceEventLogsResult(items.Select(ToDto).ToArray(), total, page, pageSize);
    }

    internal static RaceEventLogDto ToDto(RaceEventLog log)
    {
        var notification = RaceEventLogMapper.ToNotification(log);
        return new RaceEventLogDto(
            notification.Id,
            notification.Sequence,
            notification.EventId,
            notification.EventDistanceId,
            notification.EventType,
            notification.OccurredAtUtc,
            notification.RecordedAtUtc,
            notification.BibNumber,
            notification.CheckpointId,
            notification.RegistrationId,
            notification.SourceType,
            notification.SourceId,
            notification.ScanMethod);
    }

    internal static RaceEventLogType? ParseEventType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (!Enum.GetNames<RaceEventLogType>().Any(name => name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException("Loại sự kiện timeline không hợp lệ.");
        }

        return Enum.Parse<RaceEventLogType>(trimmed, ignoreCase: true);
    }

    private static DateTime? AsUtc(DateTime? value)
    {
        if (value is null || value.Value == default)
        {
            return null;
        }

        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };
    }
}

public sealed class GetRaceEventLogQueryHandler(
    IRaceEventLogRepository logs,
    IEventOrganizerAccessQuery access,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetRaceEventLogQuery, RaceEventLogDto>
{
    public async Task<RaceEventLogDto> Handle(GetRaceEventLogQuery request, CancellationToken cancellationToken)
    {
        await RaceTimelineAccess.EnsureCanViewAsync(
            request.EventId, access, sosOps, currentUser, cancellationToken);

        var log = await logs.GetByIdAsync(request.LogId, cancellationToken);
        if (log is null || log.EventId != request.EventId)
        {
            throw new DomainException("Không tìm thấy log.");
        }

        return ListRaceEventLogsQueryHandler.ToDto(log);
    }
}

public sealed class EnsureRaceTimelineAccessQueryHandler(
    IEventOrganizerAccessQuery access,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser) : IRequestHandler<EnsureRaceTimelineAccessQuery>
{
    public Task Handle(EnsureRaceTimelineAccessQuery request, CancellationToken cancellationToken)
        => RaceTimelineAccess.EnsureCanViewAsync(
            request.EventId, access, sosOps, currentUser, cancellationToken);
}
