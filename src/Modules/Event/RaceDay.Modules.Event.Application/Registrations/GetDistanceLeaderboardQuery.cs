using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Registrations;

public sealed record LeaderboardItemDto(
    int Rank,
    string EventName,
    string DistanceName,
    string Bib,
    string FullName,
    string? Gender,        // Bổ sung Giới tính
    DateTime? StartAtUtc,
    DateTime? FinishAtUtc,
    string TotalTime,
    string Pace);

public sealed record PaginatedLeaderboardDto(
    IReadOnlyList<LeaderboardItemDto> Items,
    int TotalCount,
    int TotalPages,
    int CurrentPage,
    int PageSize);

public sealed record GetDistanceLeaderboardQuery(
    Guid EventId,
    Guid DistanceId,
    string? Gender = null, // Thêm tham số lọc giới tính
    int Page = 1,
    int PageSize = 50) : IRequest<PaginatedLeaderboardDto>;

public sealed class GetDistanceLeaderboardQueryHandler(
    IRaceEventRepository events,
    IEventRegistrationRepository registrations) : IRequestHandler<GetDistanceLeaderboardQuery, PaginatedLeaderboardDto>
{
    public async Task<PaginatedLeaderboardDto> Handle(
        GetDistanceLeaderboardQuery request,
        CancellationToken cancellationToken)
    {
        var race = await events.GetByIdWithDetailsAsync(request.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        var distance = race.GetDistance(request.DistanceId);
        var allRegistrations = await registrations.ListByEventAsync(request.EventId, cancellationToken);

        // Khởi tạo query lọc VĐV hoàn thành
        var finishedRunnersQuery = allRegistrations
            .Where(r => r.DistanceId == request.DistanceId
                     && r.Status == RegistrationStatus.Finished
                     && r.ResultSeconds.HasValue);

        // Lọc thêm theo giới tính nếu có truyền lên (bỏ qua nếu truyền "All" hoặc để trống)
        if (!string.IsNullOrWhiteSpace(request.Gender) && !request.Gender.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            finishedRunnersQuery = finishedRunnersQuery.Where(r =>
                string.Equals(r.Gender, request.Gender, StringComparison.OrdinalIgnoreCase));
        }

        // Sắp xếp theo thành tích
        var finishedRunners = finishedRunnersQuery
            .OrderBy(r => r.ResultSeconds)
            .ToList();

        // Phân trang
        int totalCount = finishedRunners.Count;
        int totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)request.PageSize);

        var pagedRunners = finishedRunners
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        var leaderboard = new List<LeaderboardItemDto>();
        int rank = ((request.Page - 1) * request.PageSize) + 1;

        foreach (var r in pagedRunners)
        {
            var ts = TimeSpan.FromSeconds(r.ResultSeconds!.Value);
            var totalTimeStr = $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";

            string paceStr = "--:--";
            if (distance.DistanceKm > 0)
            {
                var paceSeconds = r.ResultSeconds.Value / distance.DistanceKm;
                var pTs = TimeSpan.FromSeconds(paceSeconds);
                paceStr = $"{(int)pTs.TotalMinutes:D2}:{pTs.Seconds:D2}";
            }

            leaderboard.Add(new LeaderboardItemDto(
                Rank: rank++,
                EventName: race.Name,
                DistanceName: distance.Name,
                Bib: r.BibNumber ?? string.Empty,
                FullName: r.FullName,
                Gender: r.Gender,       // Trả về giới tính cho UI hiển thị
                StartAtUtc: r.StartedAtUtc,
                FinishAtUtc: r.FinishedAtUtc,
                TotalTime: totalTimeStr,
                Pace: paceStr
            ));
        }

        return new PaginatedLeaderboardDto(leaderboard, totalCount, totalPages, request.Page, request.PageSize);
    }
}