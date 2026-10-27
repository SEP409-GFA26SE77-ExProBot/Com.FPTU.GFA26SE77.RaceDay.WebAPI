using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.Event.Application.Registrations;

public sealed record MyLeaderboardItemDto(
    int Rank,
    string EventName,
    string DistanceName,
    string Bib,
    string FullName,
    string? Gender,
    string TotalTime,
    string Pace);

public sealed record GetMyLeaderboardResultsQuery : IRequest<IReadOnlyList<MyLeaderboardItemDto>>;

public sealed class GetMyLeaderboardResultsQueryHandler(
   ICurrentUserAccessor currentUser,
   IEventRegistrationRepository registrations,
   IRaceEventRepository events) : IRequestHandler<GetMyLeaderboardResultsQuery, IReadOnlyList<MyLeaderboardItemDto>>
{
    public async Task<IReadOnlyList<MyLeaderboardItemDto>> Handle(
        GetMyLeaderboardResultsQuery request,
        CancellationToken cancellationToken)
    {
        // Kiểm tra an toàn: Nếu UserId null (chưa đăng nhập/token lỗi) thì trả về mảng rỗng
        if (currentUser.UserId is null)
        {
            return Array.Empty<MyLeaderboardItemDto>();
        }
        var userId = currentUser.UserId.Value;

        // 1. Lấy danh sách lượt đăng ký của Runner đang đăng nhập
        var myRegistrations = await registrations.ListByUserAsync(userId, cancellationToken);

        // 2. Chỉ lấy các lượt đăng ký đã hoàn thành (Finished)
        var finishedRegistrations = myRegistrations
            .Where(r => r.Status == RegistrationStatus.Finished && r.ResultSeconds.HasValue)
            .ToList();

        var results = new List<MyLeaderboardItemDto>();

        foreach (var myReg in finishedRegistrations)
        {
            // Lấy thông tin giải đấu và cự ly
            var race = await events.GetByIdWithDetailsAsync(myReg.EventId, cancellationToken);
            if (race is null) continue;

            var distance = race.GetDistance(myReg.DistanceId);

            // 3. Lấy tất cả VĐV cùng cự ly của giải đó để tính Thứ hạng (Rank)
            var allEventRegistrations = await registrations.ListByEventAsync(myReg.EventId, cancellationToken);

            var sameDistanceFinishedRunners = allEventRegistrations
                .Where(r => r.DistanceId == myReg.DistanceId
                         && r.Status == RegistrationStatus.Finished
                         && r.ResultSeconds.HasValue)
                .OrderBy(r => r.ResultSeconds)
                .ToList();

            // Tính Rank của runner trong cự ly này
            int rank = sameDistanceFinishedRunners.FindIndex(r => r.Id == myReg.Id) + 1;

            // Format Total Time (HH:mm:ss)
            var ts = TimeSpan.FromSeconds(myReg.ResultSeconds!.Value);
            var totalTimeStr = $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";

            // Format Pace (mm:ss /km)
            string paceStr = "--:--";
            if (distance.DistanceKm > 0)
            {
                var paceSeconds = myReg.ResultSeconds.Value / distance.DistanceKm;
                var pTs = TimeSpan.FromSeconds(paceSeconds);
                paceStr = $"{(int)pTs.TotalMinutes:D2}:{pTs.Seconds:D2}";
            }

            results.Add(new MyLeaderboardItemDto(
                Rank: rank,
                EventName: race.Name,
                DistanceName: distance.Name,
                Bib: myReg.BibNumber ?? string.Empty,
                FullName: myReg.FullName,
                Gender: myReg.Gender,
                TotalTime: totalTimeStr,
                Pace: paceStr
            ));
        }

        return results;
    }
}