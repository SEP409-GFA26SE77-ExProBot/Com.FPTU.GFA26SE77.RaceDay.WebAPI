using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Certificates;

public sealed record GetRunnerCertificateQuery(Guid RegistrationId) : IRequest<CertificateDataDto>;

public sealed class GetRunnerCertificateQueryHandler(
    IEventRegistrationRepository registrations,
    IRaceEventRepository events)
    : IRequestHandler<GetRunnerCertificateQuery, CertificateDataDto>
{
    public async Task<CertificateDataDto> Handle(GetRunnerCertificateQuery request, CancellationToken cancellationToken)
    {
        // 1. Lấy thông tin đăng ký của VĐV
        var reg = await registrations.GetByIdAsync(request.RegistrationId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy thông tin đăng ký của vận động viên.");

        // 2. Kiểm tra xem VĐV đã hoàn thành (Finished) chưa
        if (reg.Status.ToString() != "Finished" || reg.FinishedAtUtc == null)
        {
            throw new DomainException("Vận động viên chưa hoàn thành giải đấu, không thể xuất giấy chứng nhận.");
        }

        // 3. Lấy thông tin giải đấu và cự ly
        var race = await events.GetByIdWithDetailsAsync(reg.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy thông tin giải đấu.");

        var distance = race.GetDistance(reg.DistanceId);

        // 4. Tính toán thời gian & Pace dựa trên ResultSeconds hoặc khoảng thời gian Start -> Finish
        long totalSeconds = reg.ResultSeconds ?? (reg.FinishedAtUtc.Value - (reg.StartedAtUtc ?? reg.FinishedAtUtc.Value)).Ticks / TimeSpan.TicksPerSecond;
        if (totalSeconds < 0) totalSeconds = 0;

        var totalTimeSpan = TimeSpan.FromSeconds(totalSeconds);
        string totalTimeStr = totalTimeSpan.ToString(@"hh\:mm\:ss");

        // Tính Pace (phút / km)
        double totalMinutes = totalTimeSpan.TotalMinutes;
        double paceMinutes = distance.DistanceKm > 0 ? totalMinutes / distance.DistanceKm : 0;
        int pMin = (int)paceMinutes;
        int pSec = (int)((paceMinutes - pMin) * 60);
        string paceStr = $"{pMin:D2}:{pSec:D2}";

        return new CertificateDataDto(
            RunnerName: reg.FullName,
            BibNumber: reg.BibNumber ?? "N/A",
            RaceName: race.Name,
            DistanceName: distance.Name,
            DistanceKm: distance.DistanceKm,
            FinishTime: totalTimeStr,
            Pace: paceStr,
            Rank: null, // Có thể cập nhật lấy rank từ bảng xếp hạng nếu entity có hỗ trợ
            CompletedAtUtc: reg.FinishedAtUtc.Value
        );
    }
}