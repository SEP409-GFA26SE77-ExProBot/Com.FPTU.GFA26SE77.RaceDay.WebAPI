using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Registrations;

public sealed record FinishRequestDto(Guid EventId, string Bib, Guid DistanceId);

public sealed record FinishResultDto(
    Guid RegistrationId,
    Guid EventId,
    string BibNumber,
    Guid DistanceId,
    string DistanceLabel,
    double? DistanceKm,
    DateTime PassedAtUtc,
    bool AlreadyScanned,
    string Status,
    DateTime? FinishedAtUtc,
    long? ResultSeconds);

public sealed record FinishRegistrationCommand(Guid EventId, string Bib, Guid DistanceId)
    : IRequest<FinishResultDto>;

public sealed class FinishRegistrationCommandHandler(
    IRaceEventRepository events,
    IEventRegistrationRepository registrations,
    IEventStaffMemberRepository staff,
    ICurrentUserAccessor currentUser,
    IEventUnitOfWork unitOfWork) : IRequestHandler<FinishRegistrationCommand, FinishResultDto>
{
    public async Task<FinishResultDto> Handle(
        FinishRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        if (request.EventId == Guid.Empty)
        {
            throw new DomainException("Thiếu eventId.");
        }

        if (request.DistanceId == Guid.Empty)
        {
            throw new DomainException("Thiếu distanceId.");
        }

        var bib = request.Bib?.Trim();
        if (string.IsNullOrWhiteSpace(bib))
        {
            throw new DomainException("Thiếu bib.");
        }

        var race = await events.GetByIdWithDetailsAsync(request.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        var allowed = currentUser.IsAdmin
            || race.OrganizerUserId == userId
            || await staff.ExistsActiveStaffUserAsync(request.EventId, userId, cancellationToken);
        if (!allowed)
        {
            throw new DomainException("Bạn không có quyền ghi nhận finish cho giải này.");
        }

        var reg = await registrations.GetByEventAndBibAsync(request.EventId, bib, cancellationToken)
            ?? throw new DomainException($"Không tìm thấy BIB {bib} trên giải này.");

        if (reg.Status is not (RegistrationStatus.Confirmed or RegistrationStatus.Finished))
        {
            throw new DomainException("Chỉ VĐV confirmed/finished mới ghi nhận finish được.");
        }

        var distance = race.GetDistance(request.DistanceId);
        if (reg.DistanceId != distance.Id)
        {
            throw new DomainException("VĐV không đăng ký cự ly này.");
        }

        var passedAtUtc = DateTime.UtcNow;

        // Mốc tính kết quả: Ưu tiên StartedAtUtc riêng của VĐV (Net Time). Fallback về giờ chung của giải/cự ly nếu chưa quét Start.
        var startAtUtc = reg.StartedAtUtc ?? ResolveStartAtUtc(race, distance);
        if (passedAtUtc < startAtUtc)
        {
            throw new DomainException("Finish time không hợp lệ.");
        }

        var resultSeconds = (long)Math.Round((passedAtUtc - startAtUtc).TotalSeconds, MidpointRounding.AwayFromZero);
        bool alreadyFinished = !reg.Finish(passedAtUtc, resultSeconds);

        registrations.Update(reg);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new FinishResultDto(
            reg.Id,
            reg.EventId,
            reg.BibNumber ?? string.Empty,
            distance.Id,
            $"{distance.Type} · {distance.Name}",
            distance.DistanceKm,
            passedAtUtc,
            alreadyFinished,
            RegistrationDtoMapper.ToApiStatus(reg.Status),
            reg.FinishedAtUtc,
            reg.ResultSeconds);
    }

    private static DateTime ResolveStartAtUtc(RaceEvent race, EventDistance distance)
    {
        var startAt = distance.StartAt ?? race.StartTime
            ?? throw new DomainException("Giải/cự ly chưa cấu hình giờ xuất phát.");

        return DateTime.SpecifyKind(race.EventDate.ToDateTime(startAt), DateTimeKind.Utc);
    }
}
