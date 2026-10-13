using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Events.Update;

public sealed record ReplaceDistanceInput(
    string Type,
    string Name,
    double DistanceKm,
    int Capacity,
    decimal Price,
    string? Currency = null,
    string? Color = null,
    NamedGeoPointDto? StartLocation = null,
    NamedGeoPointDto? FinishLocation = null,
    double? ElevationGainM = null,
    double? MaxElevationM = null,
    double? MinElevationM = null,
    TimeOnly? StartAt = null,
    TimeOnly? EndAt = null);

public sealed record ReplaceEventDistancesCommand(
    Guid EventId,
    IReadOnlyList<ReplaceDistanceInput> Distances,
    object? TicketPricing = null) : IRequest<RaceEventDto>;

public sealed class ReplaceDistanceInputValidator : AbstractValidator<ReplaceDistanceInput>
{
    public ReplaceDistanceInputValidator()
    {
        RuleFor(x => x.Type).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DistanceKm).InclusiveBetween(0.1, 500);
        RuleFor(x => x.Capacity).InclusiveBetween(1, 500_000);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0).LessThanOrEqualTo(100_000_000);
        RuleFor(x => x.Currency).MaximumLength(10);
        RuleFor(x => x.Color).MaximumLength(30);
        When(x => x.StartAt.HasValue && x.EndAt.HasValue, () =>
            RuleFor(x => x).Must(x => x.EndAt!.Value >= x.StartAt!.Value)
                .WithMessage("Giờ kết thúc cự ly phải ≥ giờ xuất phát."));
    }
}

public sealed class ReplaceEventDistancesCommandValidator : AbstractValidator<ReplaceEventDistancesCommand>
{
    public ReplaceEventDistancesCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.Distances).NotEmpty().WithMessage("Cần ít nhất một cự ly.");
        RuleForEach(x => x.Distances).SetValidator(new ReplaceDistanceInputValidator());
    }
}

public sealed class ReplaceEventDistancesCommandHandler(
    IRaceEventRepository events,
    ICurrentUserAccessor currentUser,
    IEventUnitOfWork unitOfWork) : IRequestHandler<ReplaceEventDistancesCommand, RaceEventDto>
{
    public async Task<RaceEventDto> Handle(ReplaceEventDistancesCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var race = await events.GetByIdWithDetailsAsync(request.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        if (!currentUser.IsAdmin && race.OrganizerUserId != userId)
        {
            throw new DomainException("Bạn không phải chủ giải này.");
        }

        var distances = request.Distances.Select(d =>
        {
            var start = d.StartLocation is null
                ? new NamedGeoPoint("Start", 0, 0)
                : new NamedGeoPoint(d.StartLocation.Name, d.StartLocation.Lat, d.StartLocation.Lng);
            var finish = d.FinishLocation is null
                ? new NamedGeoPoint("Finish", 0, 0)
                : new NamedGeoPoint(d.FinishLocation.Name, d.FinishLocation.Lat, d.FinishLocation.Lng);

            return EventDistance.Create(
                race.Id,
                d.Type,
                d.Name,
                d.DistanceKm,
                d.ElevationGainM ?? 0,
                d.MaxElevationM ?? 0,
                d.MinElevationM ?? 0,
                d.Capacity,
                d.Price,
                d.Currency,
                d.Color,
                start,
                finish,
                null,
                null,
                d.StartAt,
                d.EndAt);
        }).ToList();

        race.ReplaceDistances(distances);

        if (request.TicketPricing is not null)
        {
            race.UpdateDraftProfile(
                name: null,
                tagline: null,
                location: null,
                eventDate: null,
                startTime: null,
                description: null,
                bannerImage: null,
                totalRunnersCap: null,
                organizer: null,
                rulesJson: null,
                ticketPricingJson: RaceEventDtoMapper.SerializeOptional(request.TicketPricing));
        }

        events.Update(race);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RaceEventDtoMapper.ToDetail(race);
    }
}
