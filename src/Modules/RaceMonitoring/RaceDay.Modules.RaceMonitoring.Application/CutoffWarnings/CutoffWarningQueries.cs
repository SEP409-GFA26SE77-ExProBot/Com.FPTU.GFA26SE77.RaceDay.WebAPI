using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Application.CutoffWarnings;

public sealed record CutoffWarningEventDto(Guid EventId, string EventName, int ActiveCount, int TotalCount);

public sealed record CutoffWarningListDto(
    IReadOnlyList<CutoffWarningDto> Items,
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<CutoffWarningEventDto> Events);

/// <summary>Status: Active (default) | Resolved | Dismissed | All. No eventId = every event the caller may manage.</summary>
public sealed record ListCutoffWarningsQuery(
    Guid? EventId,
    string? Status,
    string? Search,
    int Page = 1,
    int PageSize = 50) : IRequest<CutoffWarningListDto>;

public sealed class ListCutoffWarningsQueryValidator : AbstractValidator<ListCutoffWarningsQuery>
{
    public ListCutoffWarningsQueryValidator()
    {
        RuleFor(x => x.EventId).NotEqual(Guid.Empty).WithMessage("Tham chiếu giải không hợp lệ.");
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage("Trang không hợp lệ.");
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage("Kích thước trang phải từ 1 đến 100.");
        RuleFor(x => x.Search).MaximumLength(100).WithMessage("Từ khóa quá dài.");
    }
}

public sealed class ListCutoffWarningsQueryHandler(
    ICutoffWarningRepository warnings,
    CutoffWarningAccess access)
    : IRequestHandler<ListCutoffWarningsQuery, CutoffWarningListDto>
{
    public async Task<CutoffWarningListDto> Handle(ListCutoffWarningsQuery request, CancellationToken cancellationToken)
    {
        var viewer = access.CurrentViewer();
        var status = CutoffWarningMapper.ParseStatus(request.Status);
        if (request.EventId is { } eventId)
        {
            await access.EnsureCanManageAsync(eventId, viewer, cancellationToken);
        }

        var visible = new List<CutoffWarningEventDto>();
        foreach (var summary in await warnings.SummarizeEventsAsync(cancellationToken))
        {
            if (await access.CanManageAsync(summary.EventId, viewer, cancellationToken))
            {
                visible.Add(new CutoffWarningEventDto(
                    summary.EventId,
                    summary.EventName,
                    summary.ActiveCount,
                    summary.TotalCount));
            }
        }

        Guid[] eventIds = request.EventId is { } selected
            ? [selected]
            : visible.Select(e => e.EventId).ToArray();
        var page = await warnings.ListAsync(
            new CutoffWarningFilter(eventIds, status, request.Search?.Trim(), request.Page, request.PageSize),
            cancellationToken);

        return new CutoffWarningListDto(
            page.Items.Select(CutoffWarningMapper.ToDto).ToArray(),
            page.Total,
            request.Page,
            request.PageSize,
            visible);
    }
}

public sealed record CutoffWarningPassageDto(
    Guid ScanId,
    Guid CheckpointId,
    string CheckpointName,
    DateTime ScannedAtUtc,
    string ScanMethod);

/// <param name="Last">Latest recorded passage of the runner, or null if none was ever recorded.</param>
public sealed record CutoffWarningPassagesDto(
    CutoffWarningDto Warning,
    CutoffWarningPassageDto? Last,
    IReadOnlyList<CutoffWarningPassageDto> Passages);

public sealed record GetCutoffWarningPassagesQuery(Guid WarningId) : IRequest<CutoffWarningPassagesDto>;

public sealed class GetCutoffWarningPassagesQueryValidator : AbstractValidator<GetCutoffWarningPassagesQuery>
{
    public GetCutoffWarningPassagesQueryValidator()
    {
        RuleFor(x => x.WarningId).NotEmpty().WithMessage("Tham chiếu cảnh báo không hợp lệ.");
    }
}

public sealed class GetCutoffWarningPassagesQueryHandler(
    ICutoffWarningRepository warnings,
    ICheckpointScanRepository scans,
    IEventCheckpointQuery checkpoints,
    CutoffWarningAccess access)
    : IRequestHandler<GetCutoffWarningPassagesQuery, CutoffWarningPassagesDto>
{
    private const int MaxPassages = 50;

    public async Task<CutoffWarningPassagesDto> Handle(
        GetCutoffWarningPassagesQuery request,
        CancellationToken cancellationToken)
    {
        var warning = await warnings.FindAsync(request.WarningId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy cảnh báo.");
        await access.EnsureCanManageAsync(warning.EventId, access.CurrentViewer(), cancellationToken);

        var names = (await checkpoints.ListByEventAsync(warning.EventId, cancellationToken))
            .ToDictionary(c => c.CheckpointId, c => c.CheckpointName);
        var passages = (await scans.ListByRegistrationAsync(warning.RegistrationId, MaxPassages, cancellationToken))
            .Select(scan => new CutoffWarningPassageDto(
                scan.Id,
                scan.CheckpointId,
                names.GetValueOrDefault(scan.CheckpointId) ?? "Checkpoint",
                scan.ScannedAtUtc,
                scan.ScanMethod.ToString()))
            .ToArray();

        return new CutoffWarningPassagesDto(
            CutoffWarningMapper.ToDto(warning),
            passages.FirstOrDefault(),
            passages);
    }
}
