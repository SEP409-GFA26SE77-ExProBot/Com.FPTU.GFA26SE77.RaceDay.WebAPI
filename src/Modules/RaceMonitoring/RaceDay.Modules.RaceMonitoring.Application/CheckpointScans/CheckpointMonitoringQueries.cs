using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Application.CheckpointScans;

public sealed record CheckpointMonitoringStatsDto(
    Guid EventId,
    Guid DistanceId,
    int TotalRunners,
    int PassedCount,
    int NotPassedCount);

public sealed record GetCheckpointMonitoringStatsQuery(Guid EventId, Guid DistanceId)
    : IRequest<CheckpointMonitoringStatsDto>;

public sealed class GetCheckpointMonitoringStatsQueryValidator : AbstractValidator<GetCheckpointMonitoringStatsQuery>
{
    public GetCheckpointMonitoringStatsQueryValidator()
    {
        RuleFor(x => x.EventId).NotEmpty().WithMessage("Tham chiếu giải không hợp lệ.");
        RuleFor(x => x.DistanceId).NotEmpty().WithMessage("Tham chiếu cự ly không hợp lệ.");
    }
}

public sealed class GetCheckpointMonitoringStatsQueryHandler(
    IEventRegistrationRaceQuery registrations,
    IEventDistanceAccessQuery distances,
    IEventOrganizerAccessQuery access,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser)
    : IRequestHandler<GetCheckpointMonitoringStatsQuery, CheckpointMonitoringStatsDto>
{
    public async Task<CheckpointMonitoringStatsDto> Handle(
        GetCheckpointMonitoringStatsQuery request,
        CancellationToken cancellationToken)
    {
        await RaceTimelineAccess.EnsureCanViewAsync(
            request.EventId, access, sosOps, currentUser, cancellationToken);

        _ = await distances.GetAsync(request.EventId, request.DistanceId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy cự ly.");

        var eligibleRegistrations = (await registrations.ListEligibleByEventAsync(request.EventId, cancellationToken))
            .Where(registration => registration.DistanceId == request.DistanceId)
            .ToArray();

        var total = eligibleRegistrations.Length;
        var passedCount = eligibleRegistrations.Count(registration => IsPassed(registration.Status));

        return new CheckpointMonitoringStatsDto(
            request.EventId,
            request.DistanceId,
            total,
            passedCount,
            total - passedCount);
    }

    private static bool IsPassed(string status)
        => status.Equals("Finished", StringComparison.OrdinalIgnoreCase);
}

public sealed record CheckpointRunnerMonitoringItemDto(
    Guid RegistrationId,
    string BibNumber,
    string RunnerName,
    string Status,
    DateTime? PassageTime);

public sealed record CheckpointRunnerMonitoringPaginationDto(
    int Page,
    int PageSize,
    int TotalItems);

public sealed record CheckpointRunnerMonitoringListDto(
    Guid EventId,
    Guid DistanceId,
    IReadOnlyList<CheckpointRunnerMonitoringItemDto> Items,
    CheckpointRunnerMonitoringPaginationDto Pagination);

public sealed record GetCheckpointMonitoringRunnersQuery(
    Guid EventId,
    Guid DistanceId,
    string? Status,
    string? Search,
    int Page = 1,
    int PageSize = 20) : IRequest<CheckpointRunnerMonitoringListDto>;

public sealed class GetCheckpointMonitoringRunnersQueryValidator : AbstractValidator<GetCheckpointMonitoringRunnersQuery>
{
    public GetCheckpointMonitoringRunnersQueryValidator()
    {
        RuleFor(x => x.EventId).NotEmpty().WithMessage("Tham chiếu giải không hợp lệ.");
        RuleFor(x => x.DistanceId).NotEmpty().WithMessage("Tham chiếu cự ly không hợp lệ.");
    }
}

public sealed class GetCheckpointMonitoringRunnersQueryHandler(
    IEventRegistrationRaceQuery registrations,
    IEventDistanceAccessQuery distances,
    IEventOrganizerAccessQuery access,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser)
    : IRequestHandler<GetCheckpointMonitoringRunnersQuery, CheckpointRunnerMonitoringListDto>
{
    public async Task<CheckpointRunnerMonitoringListDto> Handle(
        GetCheckpointMonitoringRunnersQuery request,
        CancellationToken cancellationToken)
    {
        await RaceTimelineAccess.EnsureCanViewAsync(
            request.EventId, access, sosOps, currentUser, cancellationToken);

        _ = await distances.GetAsync(request.EventId, request.DistanceId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy cự ly.");

        var statusFilter = ParseStatus(request.Status);
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? 20 : request.PageSize;
        var keyword = request.Search?.Trim();

        var rows = (await registrations.ListEligibleByEventAsync(request.EventId, cancellationToken))
            .Where(registration => registration.DistanceId == request.DistanceId)
            .Select(registration =>
            {
                var passed = IsPassed(registration.Status);
                return new CheckpointRunnerMonitoringItemDto(
                    registration.RegistrationId,
                    registration.BibNumber,
                    registration.RunnerName,
                    passed ? "Passed" : "NotPassed",
                    passed ? registration.FinishedAtUtc : null);
            });

        rows = statusFilter switch
        {
            CheckpointRunnerStatusFilter.Passed => rows.Where(row => row.Status == "Passed"),
            CheckpointRunnerStatusFilter.NotPassed => rows.Where(row => row.Status == "NotPassed"),
            _ => rows
        };

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            rows = rows.Where(row =>
                row.BibNumber.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || row.RunnerName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        var filtered = rows
            .OrderByDescending(row => row.PassageTime.HasValue)
            .ThenBy(row => row.PassageTime ?? DateTime.MaxValue)
            .ThenBy(row => row.BibNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalItems = filtered.Count;
        var pageItems = filtered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new CheckpointRunnerMonitoringListDto(
            request.EventId,
            request.DistanceId,
            pageItems,
            new CheckpointRunnerMonitoringPaginationDto(page, pageSize, totalItems));
    }

    private static CheckpointRunnerStatusFilter ParseStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return CheckpointRunnerStatusFilter.All;
        }

        var value = raw.Trim();
        if (value.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            return CheckpointRunnerStatusFilter.All;
        }

        if (value.Equals("Passed", StringComparison.OrdinalIgnoreCase))
        {
            return CheckpointRunnerStatusFilter.Passed;
        }

        if (value.Equals("NotPassed", StringComparison.OrdinalIgnoreCase))
        {
            return CheckpointRunnerStatusFilter.NotPassed;
        }

        throw new DomainException("Trạng thái lọc không hợp lệ.");
    }

    private static bool IsPassed(string status)
        => status.Equals("Finished", StringComparison.OrdinalIgnoreCase);
}

internal enum CheckpointRunnerStatusFilter
{
    All,
    Passed,
    NotPassed
}
