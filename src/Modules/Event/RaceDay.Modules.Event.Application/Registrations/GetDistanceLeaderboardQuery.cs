using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Registrations;

public sealed record LeaderboardItemDto(
    int Rank,
    string EventName,      // Bổ sung tên giải
    string DistanceName,
    string Bib,
    string FullName,
    DateTime? StartAtUtc,
    DateTime? FinishAtUtc,
    string TotalTime,
    string Pace);

public sealed record GetDistanceLeaderboardQuery(Guid EventId, Guid DistanceId)
    : IRequest<IReadOnlyList<LeaderboardItemDto>>;

public sealed class GetDistanceLeaderboardQueryHandler(
    IRaceEventRepository events,
    IEventRegistrationRepository registrations) : IRequestHandler<GetDistanceLeaderboardQuery, IReadOnlyList<LeaderboardItemDto>>
{
    public async Task<IReadOnlyList<LeaderboardItemDto>> Handle(
        GetDistanceLeaderboardQuery request,
        CancellationToken cancellationToken)
    {
        // Lấy thông tin giải và cự ly để lấy DistanceKm phục vụ tính Pace
        var race = await events.GetByIdWithDetailsAsync(request.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        var distance = race.GetDistance(request.DistanceId);

        // Lấy tất cả đăng ký của giải
        var allRegistrations = await registrations.ListByEventAsync(request.EventId, cancellationToken);

        // Lọc các VĐV đã finish thuộc cự ly này, sắp xếp theo thời gian chạy ngắn nhất
        var finishedRunners = allRegistrations
            .Where(r => r.DistanceId == request.DistanceId
                     && r.Status == RegistrationStatus.Finished
                     && r.ResultSeconds.HasValue)
            .OrderBy(r => r.ResultSeconds)
            .ToList();

        var leaderboard = new List<LeaderboardItemDto>();
        int rank = 1;

        foreach (var r in finishedRunners)
        {
            // Format Total Time (HH:mm:ss)
            var ts = TimeSpan.FromSeconds(r.ResultSeconds!.Value);
            var totalTimeStr = $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";

            // Tính toán và Format Pace (mm:ss /km)
            string paceStr = "--:--";
            if (distance.DistanceKm > 0)
            {
                var paceSeconds = r.ResultSeconds.Value / distance.DistanceKm;
                var pTs = TimeSpan.FromSeconds(paceSeconds);
                paceStr = $"{(int)pTs.TotalMinutes:D2}:{pTs.Seconds:D2}";
            }

            leaderboard.Add(new LeaderboardItemDto(
                EventName: race.Name,           
                DistanceName: distance.Name,
                Rank: rank++,
                Bib: r.BibNumber ?? string.Empty,
                FullName: r.FullName,
                StartAtUtc: r.StartedAtUtc,
                FinishAtUtc: r.FinishedAtUtc,
                TotalTime: totalTimeStr,
                Pace: paceStr
            ));
        }

        return leaderboard;
    }
}