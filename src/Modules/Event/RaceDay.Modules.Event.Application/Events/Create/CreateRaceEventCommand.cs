using System.Text.Json;
using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Application.Validation;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Events;

/// <summary>
/// Distance payload for create-event wizard.
/// type/color/elevation are not collected — BE defaults (road / brand color).
/// </summary>
public sealed record CreateDistanceInput(
    string Name,
    double DistanceKm,
    int Capacity,
    decimal Price);

/// <summary>
/// Slim create-event command aligned with organizer permit wizard.
/// Dropped from API surface (unused by FE / not required for permit flow):
/// weather, permitDocument, ticketPricing, merchandise, rewards, route geometry, checkpoints.
/// Domain still stores empty JSON / null for those columns when reading seeded events.
/// </summary>
public sealed record CreateRaceEventCommand(
    string Name,
    string? Tagline,
    string? BannerImage,
    string Location,
    DateOnly Date,
    TimeOnly? StartTime,
    string? Description,
    int TotalRunners,
    OrganizerDto Organizer,
    object? Rules,
    IReadOnlyList<CreateDistanceInput> Distances,
    IReadOnlyList<EventDocumentDto>? Documents,
    bool SubmitForApproval,
    /// <summary>Optional map pin; omitted → (0,0) until FE collects real coords.</summary>
    GeoPointDto? Coordinates = null) : IRequest<RaceEventDto>;

public sealed class CreateRaceEventCommandValidator : AbstractValidator<CreateRaceEventCommand>
{
    public CreateRaceEventCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Tagline).MaximumLength(300);
        RuleFor(x => x.BannerImage).OptionalHttpUrl();
        RuleFor(x => x.Location).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Date).EventDate();
        RuleFor(x => x.Description).MaximumLength(10_000);
        RuleFor(x => x.TotalRunners).InclusiveBetween(0, 500_000);

        RuleFor(x => x.Organizer.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Organizer.Email).NotEmpty().EmailAddress().MaximumLength(100);
        RuleFor(x => x.Organizer.Phone).NotEmpty().MaximumLength(30).VietnamPhone();
        RuleFor(x => x.Organizer.Website).OptionalHttpUrl();
        RuleFor(x => x.Organizer.LegalEntity).MaximumLength(200);

        When(x => x.Coordinates is not null, () =>
        {
            RuleFor(x => x.Coordinates!.Lat).Latitude();
            RuleFor(x => x.Coordinates!.Lng).Longitude();
        });

        RuleFor(x => x.Distances).NotEmpty().WithMessage("Cần ít nhất một cự ly.");
        RuleForEach(x => x.Distances).SetValidator(new CreateDistanceInputValidator());

        When(x => x.Documents is { Count: > 0 }, () =>
        {
            RuleForEach(x => x.Documents!).ChildRules(doc =>
            {
                doc.RuleFor(d => d.DocumentKey).NotEmpty().MaximumLength(100);
                doc.RuleFor(d => d.Title).NotEmpty().MaximumLength(200);
                doc.RuleFor(d => d.FileName).MaximumLength(200);
                doc.RuleFor(d => d.FileUrl).OptionalHttpUrl();
            });
        });
    }
}

public sealed class CreateDistanceInputValidator : AbstractValidator<CreateDistanceInput>
{
    public CreateDistanceInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DistanceKm).InclusiveBetween(0.1, 500);
        RuleFor(x => x.Capacity).InclusiveBetween(1, 500_000);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0).LessThanOrEqualTo(100_000_000);
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

        var lat = request.Coordinates?.Lat ?? 0;
        var lng = request.Coordinates?.Lng ?? 0;

        var race = RaceEvent.CreateDraft(
            organizerUserId,
            request.Name,
            request.Location,
            request.Date,
            new GeoPoint(lat, lng),
            organizer,
            request.TotalRunners,
            request.Tagline,
            request.StartTime,
            request.Description,
            weather: null,
            permit: null,
            ticketPricingJson: null,
            merchandiseJson: null,
            rulesJson: RaceEventDtoMapper.SerializeOptional(request.Rules),
            rewardsJson: null,
            bannerImage: request.BannerImage);

        var distances = request.Distances.Select(d =>
            EventDistance.Create(
                race.Id,
                d.Name,
                d.DistanceKm,
                d.Capacity,
                d.Price,
                routeGeometryJson: "{}")).ToList();

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
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Publish sau commit → RaceEventCreatedEventHandler → IAM gán Organizer.
        await eventBus.PublishAsync(
            new RaceEventCreatedIntegrationEvent(race.Id, race.Name, race.OrganizerUserId),
            cancellationToken);
        if (race.Status == RaceEventStatus.PendingApproval)
        {
            await eventBus.PublishAsync(
                new RaceEventSubmittedIntegrationEvent(race.Id, race.Name, race.OrganizerUserId),
                cancellationToken);
        }

        return RaceEventDtoMapper.ToDetail(race);
    }
}
