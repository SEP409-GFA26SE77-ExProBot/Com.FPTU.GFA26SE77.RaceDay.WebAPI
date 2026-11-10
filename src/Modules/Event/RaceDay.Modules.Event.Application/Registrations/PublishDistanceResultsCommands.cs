using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Registrations;

public sealed record DistanceResultsPublishStatusDto(
    Guid DistanceId,
    string Name,
    double DistanceKm,
    bool ResultsPublished,
    DateTime? ResultsPublishedAtUtc,
    int FinishedCount);

public sealed record GetDistanceResultsPublishStatusQuery(
    Guid EventId,
    Guid DistanceId) : IRequest<DistanceResultsPublishStatusDto>;

public sealed record SetDistanceResultsPublishedCommand(
    Guid EventId,
    Guid DistanceId,
    bool Published) : IRequest<DistanceResultsPublishStatusDto>;

public sealed class SetDistanceResultsPublishedCommandValidator
    : AbstractValidator<SetDistanceResultsPublishedCommand>
{
    public SetDistanceResultsPublishedCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.DistanceId).NotEmpty();
    }
}

public sealed class GetDistanceResultsPublishStatusQueryHandler(
    IRaceEventRepository events,
    IEventRegistrationRepository registrations,
    ICurrentUserAccessor currentUser)
    : IRequestHandler<GetDistanceResultsPublishStatusQuery, DistanceResultsPublishStatusDto>
{
    public async Task<DistanceResultsPublishStatusDto> Handle(
        GetDistanceResultsPublishStatusQuery request,
        CancellationToken cancellationToken)
    {
        var race = await LoadAuthorizedEventAsync(
            request.EventId, events, currentUser, cancellationToken);
        var distance = race.GetDistance(request.DistanceId);
        var finished = await CountFinishedAsync(
            registrations, request.EventId, request.DistanceId, cancellationToken);
        return ToDto(distance, finished);
    }

    internal static async Task<Domain.Events.RaceEvent> LoadAuthorizedEventAsync(
        Guid eventId,
        IRaceEventRepository events,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var actor = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var race = await events.GetByIdWithDetailsAsync(eventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");
        if (!currentUser.IsAdmin && race.OrganizerUserId != actor)
        {
            throw new DomainException("Bạn không có quyền quản lý kết quả của giải này.");
        }

        return race;
    }

    internal static async Task<int> CountFinishedAsync(
        IEventRegistrationRepository registrations,
        Guid eventId,
        Guid distanceId,
        CancellationToken cancellationToken)
    {
        var all = await registrations.ListByEventAsync(eventId, cancellationToken);
        return all.Count(r =>
            r.DistanceId == distanceId
            && r.Status == RegistrationStatus.Finished
            && r.RaceStatus == RaceStatus.Finished
            && r.ResultSeconds.HasValue);
    }

    internal static DistanceResultsPublishStatusDto ToDto(
        Domain.Events.EventDistance distance,
        int finishedCount) =>
        new(
            distance.Id,
            distance.Name,
            distance.DistanceKm,
            distance.ResultsPublished,
            distance.ResultsPublishedAtUtc,
            finishedCount);
}

public sealed class SetDistanceResultsPublishedCommandHandler(
    IRaceEventRepository events,
    IEventRegistrationRepository registrations,
    IEventUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser)
    : IRequestHandler<SetDistanceResultsPublishedCommand, DistanceResultsPublishStatusDto>
{
    public async Task<DistanceResultsPublishStatusDto> Handle(
        SetDistanceResultsPublishedCommand request,
        CancellationToken cancellationToken)
    {
        var race = await GetDistanceResultsPublishStatusQueryHandler.LoadAuthorizedEventAsync(
            request.EventId, events, currentUser, cancellationToken);
        var distance = race.GetDistance(request.DistanceId);

        if (request.Published)
        {
            distance.PublishResults(DateTime.UtcNow);
        }
        else
        {
            distance.UnpublishResults();
        }

        events.Update(race);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var finished = await GetDistanceResultsPublishStatusQueryHandler.CountFinishedAsync(
            registrations, request.EventId, request.DistanceId, cancellationToken);
        return GetDistanceResultsPublishStatusQueryHandler.ToDto(distance, finished);
    }
}
