using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Application.Registrations;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Certificates;

public sealed record GetRunnerCertificateQuery(Guid RegistrationId) : IRequest<CertificateDataDto?>;

public sealed class GetRunnerCertificateQueryValidator : AbstractValidator<GetRunnerCertificateQuery>
{
    public GetRunnerCertificateQueryValidator()
    {
        RuleFor(x => x.RegistrationId).NotEmpty().WithMessage("RegistrationId không hợp lệ.");
    }
}

public sealed class GetRunnerCertificateQueryHandler(
    IEventRegistrationRepository registrations,
    IRaceEventRepository events,
    ICurrentUserAccessor currentUser)
    : IRequestHandler<GetRunnerCertificateQuery, CertificateDataDto?>
{
    public async Task<CertificateDataDto?> Handle(GetRunnerCertificateQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập để xem chứng nhận.");
        if (userId == Guid.Empty)
            throw new DomainException("Cần đăng nhập để xem chứng nhận.");

        var reg = await registrations.GetByIdAsync(request.RegistrationId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy đăng ký.");

        if (reg.UserId != userId)
        {
            throw new DomainException("Bạn không có quyền xem chứng nhận của đăng ký này.");
        }

        if (reg.Status != RegistrationStatus.Finished
            || reg.FinishedAtUtc is null
            || reg.ResultSeconds is null or < 0)
        {
            return null;
        }

        var race = await events.GetByIdWithDetailsAsync(reg.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy thông tin giải đấu.");

        var distance = race.GetDistance(reg.DistanceId);
        var elapsedSeconds = reg.StartedAtUtc.HasValue
            ? (reg.FinishedAtUtc.Value - reg.StartedAtUtc.Value).TotalSeconds
            : reg.ResultSeconds.Value;
        if (elapsedSeconds < 0 || !double.IsFinite(distance.DistanceKm) || distance.DistanceKm <= 0)
        {
            return null;
        }

        var resultSeconds = (long)Math.Round(elapsedSeconds, MidpointRounding.AwayFromZero);
        var finishTime = FormattableString.Invariant($"{resultSeconds / 3600:D2}:{resultSeconds / 60 % 60:D2}:{resultSeconds % 60:D2}");
        var paceSeconds = (long)Math.Round(elapsedSeconds / distance.DistanceKm, MidpointRounding.AwayFromZero);
        var pace = FormattableString.Invariant($"{paceSeconds / 60:D2}:{paceSeconds % 60:D2}");

        return new CertificateDataDto(
            RegistrationId: reg.Id,
            RunnerName: reg.FullName,
            EventName: race.Name,
            Bib: reg.BibNumber,
            Distance: $"{distance.Type} · {distance.Name}",
            RaceDate: race.EventDate,
            Status: RegistrationDtoMapper.ToApiStatus(reg.Status),
            FinishTime: finishTime,
            ResultSeconds: resultSeconds,
            FinishedAtUtc: reg.FinishedAtUtc.Value,
            StartedAtUtc: reg.StartedAtUtc,
            DistanceKm: distance.DistanceKm,
            Pace: pace
        );
    }
}
