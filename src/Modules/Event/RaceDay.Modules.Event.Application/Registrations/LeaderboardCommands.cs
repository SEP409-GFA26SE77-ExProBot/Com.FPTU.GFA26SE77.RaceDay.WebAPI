using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Registrations;

/// <summary>Payload for adding or replacing a runner's leaderboard result.</summary>
public sealed record UpsertLeaderboardResultRequest
{
    public required Guid UserId { get; init; }
    public required string TotalTime { get; init; }
    public required DateTimeOffset StartAtUtc { get; init; }
    public required DateTimeOffset FinishedAtUtc { get; init; }
}

/// <summary>Payload for updating a runner's leaderboard result.</summary>
public sealed record UpdateLeaderboardResultRequest
{
    public required string TotalTime { get; init; }
    public required DateTimeOffset StartAtUtc { get; init; }
    public required DateTimeOffset FinishedAtUtc { get; init; }
}

public sealed record CreateLeaderboardResultCommand(
    Guid EventId,
    Guid DistanceId,
    Guid UserId,
    string TotalTime,
    DateTimeOffset StartAtUtc,
    DateTimeOffset FinishedAtUtc) : IRequest<LeaderboardItemDto>;

public sealed record UpdateLeaderboardResultCommand(
    Guid EventId,
    Guid DistanceId,
    Guid UserId,
    string TotalTime,
    DateTimeOffset StartAtUtc,
    DateTimeOffset FinishedAtUtc) : IRequest<LeaderboardItemDto>;

public sealed record DeleteLeaderboardResultCommand(
    Guid EventId,
    Guid DistanceId,
    Guid UserId) : IRequest;

internal static class LeaderboardCommandHelper
{
    public static async Task<(RaceDay.Modules.Event.Domain.Events.RaceEvent Race, EventRegistration Registration)> LoadAuthorizedAsync(
        Guid eventId,
        Guid distanceId,
        Guid userId,
        IRaceEventRepository events,
        IEventRegistrationRepository registrations,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var race = await events.GetByIdWithDetailsAsync(eventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        if (!currentUser.IsAdmin && race.OrganizerUserId != actorUserId)
        {
            throw new DomainException("Bạn không có quyền quản lý bảng xếp hạng của giải này.");
        }

        var registration = await registrations.GetByEventDistanceAndUserAsync(
            eventId, distanceId, userId, cancellationToken);
        if (registration is null)
        {
            throw new DomainException("Không tìm thấy đăng ký cho UserId này trong cự ly.");
        }
        if (registration.EventId != eventId || registration.DistanceId != distanceId)
        {
            throw new DomainException("Đăng ký không thuộc giải hoặc cự ly này.");
        }

        race.GetDistance(distanceId);
        return (race, registration);
    }

    public static LeaderboardItemDto ToDto(
        RaceDay.Modules.Event.Domain.Events.RaceEvent race,
        RaceDay.Modules.Event.Domain.Events.EventDistance distance,
        EventRegistration registration,
        int rank)
    {
        var seconds = registration.ResultSeconds
            ?? throw new DomainException("Đăng ký chưa có kết quả.");
        var total = TimeSpan.FromSeconds(seconds);
        var pace = distance.DistanceKm > 0
            ? TimeSpan.FromSeconds(seconds / distance.DistanceKm)
            : TimeSpan.Zero;

        return new LeaderboardItemDto(
            rank,
            race.Name,
            distance.Name,
            registration.BibNumber ?? string.Empty,
            registration.FullName,
            registration.Gender,
            registration.StartedAtUtc,
            registration.FinishedAtUtc,
            $"{(int)total.TotalHours:D2}:{total.Minutes:D2}:{total.Seconds:D2}",
            distance.DistanceKm > 0 ? $"{(int)pace.TotalMinutes:D2}:{pace.Seconds:D2}" : "--:--");
    }

    public static long ParseTotalTime(string totalTime, DateTimeOffset startAtUtc, DateTimeOffset finishAtUtc)
    {
        if (!TimeSpan.TryParseExact(totalTime, @"hh\:mm\:ss",
                System.Globalization.CultureInfo.InvariantCulture, out var duration)
            || duration < TimeSpan.Zero
            || duration > TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59))
        {
            throw new DomainException("totalTime phải có định dạng HH:mm:ss và nằm trong khoảng 00:00:00 đến 23:59:59.");
        }

        var actualDuration = finishAtUtc.UtcDateTime - startAtUtc.UtcDateTime;
        if (actualDuration != duration)
        {
            throw new DomainException("totalTime phải khớp chính xác với finishAtUtc - startAtUtc.");
        }

        return checked((long)duration.TotalSeconds);
    }

    public static async Task<int> GetRankAsync(
        Guid eventId,
        Guid distanceId,
        Guid registrationId,
        IEventRegistrationRepository registrations,
        CancellationToken cancellationToken)
    {
        var rank = 0;
        var results = await registrations.ListByEventAsync(eventId, cancellationToken);
        foreach (var result in results
            .Where(x => x.DistanceId == distanceId
                && x.Status == RegistrationStatus.Finished
                && x.RaceStatus == RaceStatus.Finished
                && x.ResultSeconds.HasValue)
            .OrderBy(x => x.ResultSeconds)
            .ThenBy(x => x.Id))
        {
            rank++;
            if (result.Id == registrationId)
            {
                return rank;
            }
        }

        throw new DomainException("Không thể tính thứ hạng của kết quả.");
    }
}

public sealed class CreateLeaderboardResultCommandHandler(
    IRaceEventRepository events,
    IEventRegistrationRepository registrations,
    ICurrentUserAccessor currentUser,
    IEventUnitOfWork unitOfWork)
    : IRequestHandler<CreateLeaderboardResultCommand, LeaderboardItemDto>
{
    public async Task<LeaderboardItemDto> Handle(CreateLeaderboardResultCommand request, CancellationToken cancellationToken)
    {
        var (race, registration) = await LeaderboardCommandHelper.LoadAuthorizedAsync(
            request.EventId, request.DistanceId, request.UserId,
            events, registrations, currentUser, cancellationToken);
        var distance = race.GetDistance(request.DistanceId);
        var resultSeconds = LeaderboardCommandHelper.ParseTotalTime(
            request.TotalTime, request.StartAtUtc, request.FinishedAtUtc);
        registration.SetLeaderboardResult(
            request.StartAtUtc.UtcDateTime, request.FinishedAtUtc.UtcDateTime, resultSeconds);
        registrations.Update(registration);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var rank = await LeaderboardCommandHelper.GetRankAsync(
            request.EventId, request.DistanceId, registration.Id, registrations, cancellationToken);
        return LeaderboardCommandHelper.ToDto(race, distance, registration, rank);
    }
}

public sealed class UpdateLeaderboardResultCommandHandler(
    IRaceEventRepository events,
    IEventRegistrationRepository registrations,
    ICurrentUserAccessor currentUser,
    IEventUnitOfWork unitOfWork)
    : IRequestHandler<UpdateLeaderboardResultCommand, LeaderboardItemDto>
{
    public async Task<LeaderboardItemDto> Handle(UpdateLeaderboardResultCommand request, CancellationToken cancellationToken)
    {
        var (race, registration) = await LeaderboardCommandHelper.LoadAuthorizedAsync(
            request.EventId, request.DistanceId, request.UserId,
            events, registrations, currentUser, cancellationToken);
        var distance = race.GetDistance(request.DistanceId);
        var resultSeconds = LeaderboardCommandHelper.ParseTotalTime(
            request.TotalTime, request.StartAtUtc, request.FinishedAtUtc);
        registration.SetLeaderboardResult(
            request.StartAtUtc.UtcDateTime, request.FinishedAtUtc.UtcDateTime, resultSeconds);
        registrations.Update(registration);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var rank = await LeaderboardCommandHelper.GetRankAsync(
            request.EventId, request.DistanceId, registration.Id, registrations, cancellationToken);
        return LeaderboardCommandHelper.ToDto(race, distance, registration, rank);
    }
}

public sealed class DeleteLeaderboardResultCommandHandler(
    IRaceEventRepository events,
    IEventRegistrationRepository registrations,
    ICurrentUserAccessor currentUser,
    IEventUnitOfWork unitOfWork)
    : IRequestHandler<DeleteLeaderboardResultCommand>
{
    public async Task Handle(DeleteLeaderboardResultCommand request, CancellationToken cancellationToken)
    {
        var (_, registration) = await LeaderboardCommandHelper.LoadAuthorizedAsync(
            request.EventId, request.DistanceId, request.UserId,
            events, registrations, currentUser, cancellationToken);
        registration.RemoveLeaderboardResult();
        registrations.Update(registration);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
