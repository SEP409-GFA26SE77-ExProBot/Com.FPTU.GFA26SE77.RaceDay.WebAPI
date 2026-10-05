using System.Text.Json;
using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Events;

public sealed record CreateDistanceInput(
    string Type,
    string Name,
    double DistanceKm,
    double ElevationGainM,
    double MaxElevationM,
    double MinElevationM,
    int Capacity,
    decimal Price,
    string? Currency,
    string? Color,
    NamedGeoPointDto StartLocation,
    NamedGeoPointDto FinishLocation,
    IReadOnlyList<CheckpointDto>? Checkpoints,
    JsonElement? OsrmWaypoints,
    JsonElement? LeafletPolyline,
    JsonElement? GeoJson);

public sealed record CreateRaceEventCommand(
    string Name,
    string? Tagline,
    string? BannerImage,
    string Location,
    GeoPointDto Coordinates,
    DateOnly Date,
    TimeOnly? StartTime,
    string? Description,
    int TotalRunners,
    OrganizerDto Organizer,
    WeatherDto? Weather,
    PermitDocumentDto? PermitDocument,
    object? TicketPricing,
    object? Merchandise,
    object? Rules,
    object? Rewards,
    IReadOnlyList<CreateDistanceInput> Distances,
    IReadOnlyList<EventDocumentDto>? Documents,
    bool SubmitForApproval) : IRequest<RaceEventDto>;

public sealed class CreateRaceEventCommandValidator : AbstractValidator<CreateRaceEventCommand>
{
    public CreateRaceEventCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Location).NotEmpty().MaximumLength(300);
        RuleFor(x => x.TotalRunners).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Organizer.Name).NotEmpty();
        RuleFor(x => x.Organizer.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Distances).NotEmpty();
    }
}

public sealed class CreateRaceEventCommandHandler(
    IRaceEventRepository events,
    ICurrentUserAccessor currentUser,
    IEventUnitOfWork unitOfWork,
    IEventBus eventBus) : IRequestHandler<CreateRaceEventCommand, RaceEventDto>
{
    public async Task<RaceEventDto> Handle(CreateRaceEventCommand request, CancellationToken cancellationToken)
    {
        var organizerUserId = currentUser.UserId
            ?? throw new DomainException("Cần đăng nhập để tạo giải.");

        var bank = request.Organizer.BeneficiaryAccount;
        var organizer = OrganizerInfo.Create(
            request.Organizer.Name,
            request.Organizer.Email,
            request.Organizer.Phone,
            request.Organizer.LegalEntity,
            request.Organizer.Website,
            request.Organizer.Verified,
            bank?.AccountHolder,
            bank?.AccountNumber,
            bank?.BankName,
            bank?.Branch,
            bank?.SwiftCode,
            bank?.TransferNote);

        PermitDocument? permit = null;
        if (request.PermitDocument is not null)
        {
            permit = PermitDocument.Create(
                request.PermitDocument.FileUrl,
                request.PermitDocument.FileName,
                request.PermitDocument.PermitNumber,
                request.PermitDocument.IssuingAuthority,
                request.PermitDocument.IssueDate,
                request.PermitDocument.ValidUntil);
        }

        WeatherInfo? weather = request.Weather is null
            ? null
            : new WeatherInfo(request.Weather.Temp, request.Weather.Condition, request.Weather.Humidity);

        var race = RaceEvent.CreateDraft(
            organizerUserId,
            request.Name,
            request.Location,
            request.Date,
            new GeoPoint(request.Coordinates.Lat, request.Coordinates.Lng),
            organizer,
            request.TotalRunners,
            request.Tagline,
            request.StartTime,
            request.Description,
            weather,
            permit,
            RaceEventDtoMapper.SerializeOptional(request.TicketPricing),
            RaceEventDtoMapper.SerializeOptional(request.Merchandise),
            RaceEventDtoMapper.SerializeOptional(request.Rules),
            RaceEventDtoMapper.SerializeOptional(request.Rewards),
            request.BannerImage);

        var distances = request.Distances.Select(d =>
        {
            var route = JsonSerializer.Serialize(new
            {
                osrmWaypoints = d.OsrmWaypoints,
                leafletPolyline = d.LeafletPolyline,
                geoJson = d.GeoJson
            });

            var cps = (d.Checkpoints ?? Array.Empty<CheckpointDto>())
                .Select(c => (
                    c.Id,
                    c.Name,
                    c.Km,
                    c.Lat,
                    c.Lng,
                    RaceEventDtoMapper.ParseCheckpointType(c.Type),
                    c.Cutoff,
                    c.Elevation))
                .ToList();

            return EventDistance.Create(
                race.Id,
                d.Type,
                d.Name,
                d.DistanceKm,
                d.ElevationGainM,
                d.MaxElevationM,
                d.MinElevationM,
                d.Capacity,
                d.Price,
                d.Currency,
                d.Color,
                new NamedGeoPoint(d.StartLocation.Name, d.StartLocation.Lat, d.StartLocation.Lng),
                new NamedGeoPoint(d.FinishLocation.Name, d.FinishLocation.Lat, d.FinishLocation.Lng),
                route,
                cps);
        }).ToList();

        race.ReplaceDistances(distances);

        if (request.Documents is { Count: > 0 })
        {
            race.ReplaceDocuments(request.Documents.Select(doc =>
                EventDocument.Create(race.Id, doc.DocumentKey, doc.Title, doc.FileName, doc.FileUrl, doc.Required)));
        }

        if (request.SubmitForApproval)
        {
            race.SubmitForApproval();
        }

        await events.AddAsync(race, cancellationToken);
        await eventBus.PublishAsync(
            new RaceEventCreatedIntegrationEvent(race.Id, race.Name, race.OrganizerUserId),
            cancellationToken);
        if (race.Status == RaceEventStatus.PendingApproval)
        {
            await eventBus.PublishAsync(
                new RaceEventSubmittedIntegrationEvent(race.Id, race.Name, race.OrganizerUserId),
                cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RaceEventDtoMapper.ToDetail(race);
    }
}
