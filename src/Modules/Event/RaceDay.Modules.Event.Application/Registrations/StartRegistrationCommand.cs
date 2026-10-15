using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Registrations;

public sealed record StartRequestDto(Guid EventId, string Bib, Guid CheckpointId);

public sealed record StartResultDto(
    Guid RegistrationId,
    Guid EventId,
    string BibNumber,
    Guid CheckpointId,
    string CheckpointName,
    DateTime StartedAtUtc,
    bool AlreadyStarted);

public sealed record StartRegistrationCommand(Guid EventId, string Bib, Guid CheckpointId)
    : IRequest<StartResultDto>;

public sealed class StartRegistrationCommandHandler(
    IRaceEventRepository events,
    IEventRegistrationRepository registrations,
    IRegistrationCheckpointPassageRepository checkpointPassages,
    IEventStaffMemberRepository staff,
    ICurrentUserAccessor currentUser,
    IEventUnitOfWork unitOfWork) : IRequestHandler<StartRegistrationCommand, StartResultDto>
{
    public async Task<StartResultDto> Handle(
        StartRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        if (request.EventId == Guid.Empty) throw new DomainException("Thiếu eventId.");
        if (request.CheckpointId == Guid.Empty) throw new DomainException("Thiếu checkpointId.");

        var bib = request.Bib?.Trim();
        if (string.IsNullOrWhiteSpace(bib)) throw new DomainException("Thiếu bib.");

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
            throw new DomainException("Chỉ VĐV confirmed mới ghi nhận xuất phát được.");
        }

        var distance = race.GetDistance(reg.DistanceId);
        var checkpoint = distance.Checkpoints.FirstOrDefault(c => c.Id == request.CheckpointId)
            ?? throw new DomainException("Checkpoint không thuộc cự ly của VĐV.");

        // Lấy hoặc tạo lượt qua vạch Start
        var existingPassage = await checkpointPassages.GetByRegistrationCheckpointAsync(
            reg.Id, checkpoint.Id, cancellationToken);

        if (existingPassage is not null)
        {
            return new StartResultDto(reg.Id, reg.EventId, reg.BibNumber ?? string.Empty, checkpoint.Id, checkpoint.Name, existingPassage.PassedAtUtc, true);
        }

        var passedAtUtc = DateTime.UtcNow;

        // 1. Lưu vết passage
        var passage = RegistrationCheckpointPassage.Create(
            reg.Id, reg.EventId, reg.DistanceId, checkpoint.Id, passedAtUtc);
        await checkpointPassages.AddAsync(passage, cancellationToken);

        // 2. Cập nhật StartedAtUtc cho VĐV
        reg.Start(passedAtUtc);
        registrations.Update(reg);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new StartResultDto(reg.Id, reg.EventId, reg.BibNumber ?? string.Empty, checkpoint.Id, checkpoint.Name, passedAtUtc, false);
    }
}