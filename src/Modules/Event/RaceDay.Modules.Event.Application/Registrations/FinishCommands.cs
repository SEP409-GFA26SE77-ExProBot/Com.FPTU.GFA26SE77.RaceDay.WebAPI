using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Registrations;

public sealed record FinishRequestDto(Guid EventId, string Bib, Guid CheckpointId);

public sealed record FinishResultDto(
    Guid RegistrationId,
    Guid EventId,
    string BibNumber,
    Guid CheckpointId,
    string CheckpointName,
    string CheckpointType,
    DateTime PassedAtUtc,
    bool AlreadyScanned,
    string Status,
    DateTime? FinishedAtUtc,
    long? ResultSeconds,
    string? DistanceLabel,
    double? DistanceKm);

public sealed record FinishRegistrationCommand(Guid EventId, string Bib, Guid CheckpointId)
    : IRequest<FinishResultDto>;

public sealed class FinishRegistrationCommandHandler(
    IRaceEventRepository events,
    IEventRegistrationRepository registrations,
    IRegistrationCheckpointPassageRepository checkpointPassages,
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

        if (request.CheckpointId == Guid.Empty)
        {
            throw new DomainException("Thiếu checkpointId.");
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
            throw new DomainException("Bạn không có quyền ghi nhận checkpoint cho giải này.");
        }

        var reg = await registrations.GetByEventAndBibAsync(request.EventId, bib, cancellationToken)
            ?? throw new DomainException($"Không tìm thấy BIB {bib} trên giải này.");

        if (reg.Status is not (RegistrationStatus.Confirmed or RegistrationStatus.Finished))
        {
            throw new DomainException("Chỉ VĐV confirmed/finished mới ghi nhận checkpoint được.");
        }

        var distance = race.GetDistance(reg.DistanceId);
        var checkpoint = distance.Checkpoints.FirstOrDefault(c => c.Id == request.CheckpointId)
            ?? throw new DomainException("Checkpoint không thuộc cự ly của VĐV.");

        var existingPassage = await checkpointPassages.GetByRegistrationCheckpointAsync(
            reg.Id,
            checkpoint.Id,
            cancellationToken);
        if (existingPassage is not null)
        {
            return BuildResult(reg, distance, checkpoint, existingPassage.PassedAtUtc, true);
        }

        var passedAtUtc = DateTime.UtcNow;
        var passage = RegistrationCheckpointPassage.Create(
            reg.Id,
            reg.EventId,
            reg.DistanceId,
            checkpoint.Id,
            passedAtUtc);
        await checkpointPassages.AddAsync(passage, cancellationToken);

        if (checkpoint.Type == CheckpointType.Finish)
        {
            // Ưu tiên giờ xuất phát riêng của VĐV (StartedAtUtc / Net Time). Fallback về giờ xuất phát chung nếu chưa quét vạch Start.
            var startAtUtc = reg.StartedAtUtc ?? ResolveStartAtUtc(race, distance);
            if (passedAtUtc < startAtUtc)
            {
                throw new DomainException("Finish time không hợp lệ.");
            }

            var resultSeconds = (long)Math.Round((passedAtUtc - startAtUtc).TotalSeconds, MidpointRounding.AwayFromZero);
            reg.Finish(passedAtUtc, resultSeconds);
            registrations.Update(reg);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BuildResult(reg, distance, checkpoint, passedAtUtc, false);
    }

    private static DateTime ResolveStartAtUtc(RaceEvent race, EventDistance distance)
    {
        var startAt = distance.StartAt ?? race.StartTime
            ?? throw new DomainException("Giải/cự ly chưa cấu hình giờ xuất phát.");

        return DateTime.SpecifyKind(race.EventDate.ToDateTime(startAt), DateTimeKind.Utc);
    }

    private static FinishResultDto BuildResult(
        EventRegistration reg,
        EventDistance distance,
        EventCheckpoint checkpoint,
        DateTime passedAtUtc,
        bool alreadyScanned)
        => new(
            reg.Id,
            reg.EventId,
            reg.BibNumber ?? string.Empty,
            checkpoint.Id,
            checkpoint.Name,
            checkpoint.Type.ToString(),
            passedAtUtc,
            alreadyScanned,
            RegistrationDtoMapper.ToApiStatus(reg.Status),
            reg.FinishedAtUtc,
            reg.ResultSeconds,
            $"{distance.Type} · {distance.Name}",
            distance.DistanceKm);
}