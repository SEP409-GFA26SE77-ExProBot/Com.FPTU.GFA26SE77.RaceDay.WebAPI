using FluentValidation;
using MediatR;
using RaceDay.Modules.Device.Application.Abstractions;
using RaceDay.Modules.Device.Contracts.Events;
using RaceDay.Modules.Device.Domain.Sos;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Application.Messaging;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Device.Application.Sos;

public sealed record SosLocationInput(
    double Lat,
    double Lng,
    double? Accuracy,
    int? Battery,
    DateTime RecordedAtUtc);

public sealed record CreateSosRequest(
    Guid Id,
    double Lat,
    double Lng,
    double? Accuracy,
    DateTime RecordedAtUtc,
    int? Battery);

public sealed record SosAlertDto(
    Guid Id,
    Guid RunnerUserId,
    Guid? EventId,
    string Status,
    DateTime CreatedAtUtc,
    double? LastLat,
    double? LastLng,
    int? Battery,
    string? Bib);

public sealed record CreateSosCommand(CreateSosRequest Body, Guid LinkedDeviceId, Guid RunnerUserId)
    : IRequest<SosAlertDto>;

public sealed class CreateSosCommandValidator : AbstractValidator<CreateSosCommand>
{
    public CreateSosCommandValidator()
    {
        RuleFor(x => x.Body).NotNull();
        RuleFor(x => x.Body.Id).NotEmpty();
        RuleFor(x => x.Body.Lat).InclusiveBetween(-90, 90);
        RuleFor(x => x.Body.Lng).InclusiveBetween(-180, 180);
        RuleFor(x => x.Body.Accuracy).GreaterThanOrEqualTo(0).When(x => x.Body.Accuracy is not null);
        RuleFor(x => x.Body.Battery).InclusiveBetween(0, 100).When(x => x.Body.Battery is not null);
        RuleFor(x => x.Body.RecordedAtUtc).NotEmpty();
        RuleFor(x => x.LinkedDeviceId).NotEmpty();
        RuleFor(x => x.RunnerUserId).NotEmpty();
    }
}

public sealed class CreateSosCommandHandler(
    ISosAlertRepository alerts,
    ILinkedDeviceRepository linkedDevices,
    IDeviceUnitOfWork unitOfWork,
    IRunnerRaceDayQuery raceDay,
    IPublisher publisher) : IRequestHandler<CreateSosCommand, SosAlertDto>
{
    public async Task<SosAlertDto> Handle(CreateSosCommand request, CancellationToken cancellationToken)
    {
        var linked = await linkedDevices.GetByIdAsync(request.LinkedDeviceId, cancellationToken)
            ?? throw new DomainException("Thiết bị không hợp lệ.");
        linked.EnsureActive();
        if (linked.RunnerUserId != request.RunnerUserId)
        {
            throw new DomainException("Token thiết bị không khớp runner.");
        }

        var body = request.Body;
        var existing = await alerts.GetByIdAsync(body.Id, cancellationToken);
        if (existing is not null)
        {
            if (existing.RunnerUserId != request.RunnerUserId)
            {
                throw new DomainException("SOS không thuộc runner này.");
            }

            var loc = SosLocation.Create(
                existing.Id, body.Lat, body.Lng, body.Accuracy, body.Battery, body.RecordedAtUtc);
            await alerts.AddLocationsAsync([loc], cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(existing, null);
        }

        var race = await raceDay.GetPrimaryAsync(request.RunnerUserId, cancellationToken);
        var first = SosLocation.Create(body.Id, body.Lat, body.Lng, body.Accuracy, body.Battery, body.RecordedAtUtc);
        var alert = SosAlert.Open(body.Id, request.RunnerUserId, request.LinkedDeviceId, race?.EventId, first);
        await alerts.AddAsync(alert, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await publisher.Publish(
            new IntegrationEventOccurred(
                new SosOpenedIntegrationEvent(alert.Id, alert.RunnerUserId, alert.EventId, body.Lat, body.Lng)),
            cancellationToken);

        return ToDto(alert, race?.BibNumber);
    }

    internal static SosAlertDto ToDto(SosAlert a, string? bib)
    {
        var last = a.Locations.OrderByDescending(x => x.RecordedAtUtc).FirstOrDefault();
        return new SosAlertDto(
            a.Id,
            a.RunnerUserId,
            a.EventId,
            a.Status.ToString(),
            a.CreatedAtUtc,
            last?.Lat,
            last?.Lng,
            last?.Battery,
            bib);
    }
}

public sealed record AppendSosLocationsRequest(IReadOnlyList<SosLocationInput> Locations);

public sealed record AppendSosLocationsCommand(
    Guid SosId,
    AppendSosLocationsRequest Body,
    Guid RunnerUserId) : IRequest;

public sealed class AppendSosLocationsCommandValidator : AbstractValidator<AppendSosLocationsCommand>
{
    public AppendSosLocationsCommandValidator()
    {
        RuleFor(x => x.SosId).NotEmpty();
        RuleFor(x => x.Body).NotNull();
        RuleFor(x => x.Body.Locations).NotEmpty().Must(x => x.Count <= 100);
        RuleForEach(x => x.Body.Locations).ChildRules(loc =>
        {
            loc.RuleFor(l => l.Lat).InclusiveBetween(-90, 90);
            loc.RuleFor(l => l.Lng).InclusiveBetween(-180, 180);
        });
    }
}

public sealed class AppendSosLocationsCommandHandler(
    ISosAlertRepository alerts,
    IDeviceUnitOfWork unitOfWork) : IRequestHandler<AppendSosLocationsCommand>
{
    public async Task Handle(AppendSosLocationsCommand request, CancellationToken cancellationToken)
    {
        var alert = await alerts.GetByIdAsync(request.SosId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy SOS.");
        if (alert.RunnerUserId != request.RunnerUserId)
        {
            throw new DomainException("SOS không thuộc runner này.");
        }

        if (alert.Status == SosStatus.Resolved)
        {
            throw new DomainException("SOS đã đóng.");
        }

        // Insert thẳng vào sos_locations — không mutate collection navigation (tránh EF concurrency).
        var rows = request.Body.Locations
            .Select(item => SosLocation.Create(
                alert.Id, item.Lat, item.Lng, item.Accuracy, item.Battery, item.RecordedAtUtc))
            .ToList();
        await alerts.AddLocationsAsync(rows, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed record ListEventSosQuery(Guid EventId, string? Status, int Take = 100)
    : IRequest<IReadOnlyList<SosAlertDto>>;

public sealed class ListEventSosQueryHandler(
    ISosAlertRepository alerts,
    IEventOrganizerAccessQuery access,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser,
    IRunnerRaceDayQuery raceDay) : IRequestHandler<ListEventSosQuery, IReadOnlyList<SosAlertDto>>
{
    public async Task<IReadOnlyList<SosAlertDto>> Handle(
        ListEventSosQuery request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        _ = await access.GetAsync(request.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        if (!currentUser.IsAdmin
            && !await sosOps.CanManageSosAsync(request.EventId, userId, cancellationToken))
        {
            throw new DomainException("Chỉ organizer của giải hoặc Coordinator Active mới xem SOS.");
        }

        SosStatus? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status)
            && Enum.TryParse<SosStatus>(request.Status, ignoreCase: true, out var parsed))
        {
            status = parsed;
        }

        var take = request.Take is < 1 or > 500 ? 100 : request.Take;
        var list = await alerts.ListByEventAsync(request.EventId, status, take, cancellationToken);
        var result = new List<SosAlertDto>(list.Count);
        foreach (var a in list)
        {
            var race = await raceDay.GetForEventAsync(a.RunnerUserId, request.EventId, cancellationToken);
            result.Add(CreateSosCommandHandler.ToDto(a, race?.BibNumber));
        }

        return result;
    }
}

public sealed record AckSosCommand(Guid SosId) : IRequest<SosAlertDto>;
public sealed record ResolveSosCommand(Guid SosId) : IRequest<SosAlertDto>;

public sealed class AckSosCommandHandler(
    ISosAlertRepository alerts,
    IDeviceUnitOfWork unitOfWork,
    IEventOrganizerAccessQuery access,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser) : IRequestHandler<AckSosCommand, SosAlertDto>
{
    public async Task<SosAlertDto> Handle(AckSosCommand request, CancellationToken cancellationToken)
    {
        var alert = await EnsureCanManage(
            request.SosId, alerts, access, sosOps, currentUser, cancellationToken);
        alert.Acknowledge();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CreateSosCommandHandler.ToDto(alert, null);
    }

    internal static async Task<SosAlert> EnsureCanManage(
        Guid sosId,
        ISosAlertRepository alerts,
        IEventOrganizerAccessQuery access,
        IEventSosOpsAccessQuery sosOps,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var alert = await alerts.GetByIdAsync(sosId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy SOS.");
        if (alert.EventId is null)
        {
            if (!currentUser.IsAdmin)
            {
                throw new DomainException("SOS chưa gắn giải — chỉ Admin xử lý.");
            }

            return alert;
        }

        _ = await access.GetAsync(alert.EventId.Value, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        if (!currentUser.IsAdmin
            && !await sosOps.CanManageSosAsync(alert.EventId.Value, userId, cancellationToken))
        {
            throw new DomainException("Chỉ organizer của giải hoặc Coordinator Active mới xử lý SOS.");
        }

        return alert;
    }
}

public sealed class ResolveSosCommandHandler(
    ISosAlertRepository alerts,
    IDeviceUnitOfWork unitOfWork,
    IEventOrganizerAccessQuery access,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser) : IRequestHandler<ResolveSosCommand, SosAlertDto>
{
    public async Task<SosAlertDto> Handle(ResolveSosCommand request, CancellationToken cancellationToken)
    {
        var alert = await AckSosCommandHandler.EnsureCanManage(
            request.SosId, alerts, access, sosOps, currentUser, cancellationToken);
        alert.Resolve();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CreateSosCommandHandler.ToDto(alert, null);
    }
}
