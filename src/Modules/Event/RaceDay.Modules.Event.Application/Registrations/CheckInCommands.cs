using System.Text.Json;
using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Registrations;

public sealed record CheckInRequestDto(Guid EventId, string? Bib, string? QrPayload);

public sealed record CheckInResultDto(
    Guid RegistrationId,
    Guid EventId,
    string BibNumber,
    string FullName,
    string Status,
    DateTime CheckedInAtUtc,
    bool AlreadyCheckedIn,
    string? DistanceLabel,
    double? DistanceKm,
    string RaceStatus = "not_started",
    string BibStatus = "normal",
    long Version = 0);

public sealed record CheckInRegistrationCommand(Guid EventId, string? Bib, string? QrPayload)
    : IRequest<CheckInResultDto>;

public sealed class CheckInRegistrationCommandHandler(
    IRaceEventRepository events,
    IEventRegistrationRepository registrations,
    IEventStaffMemberRepository staff,
    ICurrentUserAccessor currentUser,
    IEventUnitOfWork unitOfWork) : IRequestHandler<CheckInRegistrationCommand, CheckInResultDto>
{
    public async Task<CheckInResultDto> Handle(
        CheckInRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        if (request.EventId == Guid.Empty)
        {
            throw new DomainException("Thiếu eventId.");
        }

        var race = await events.GetByIdWithDetailsAsync(request.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        var allowed = currentUser.IsAdmin
            || race.OrganizerUserId == userId
            || await staff.ExistsActiveStaffUserAsync(request.EventId, userId, cancellationToken);
        if (!allowed)
        {
            throw new DomainException("Bạn không có quyền check-in giải này.");
        }

        var reg = await ResolveRegistrationAsync(
            registrations,
            request.EventId,
            request.Bib,
            request.QrPayload,
            cancellationToken);

        var firstTime = reg.CheckIn(userId);
        if (firstTime)
        {
            registrations.Update(reg);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        string? distanceLabel = null;
        double? distanceKm = null;
        try
        {
            var distance = race.GetDistance(reg.DistanceId);
            distanceLabel = $"{distance.Type} · {distance.Name}";
            distanceKm = distance.DistanceKm;
        }
        catch
        {
            /* ignore */
        }

        return new CheckInResultDto(
            reg.Id,
            reg.EventId,
            reg.BibNumber ?? string.Empty,
            reg.FullName,
            RegistrationDtoMapper.ToApiStatus(reg),
            reg.CheckedInAtUtc!.Value,
            AlreadyCheckedIn: !firstTime,
            distanceLabel,
            distanceKm,
            RegistrationDtoMapper.ToApiRaceStatus(reg.RaceStatus),
            reg.BibStatus.ToString().ToLowerInvariant(), reg.Version);
    }

    private static async Task<EventRegistration> ResolveRegistrationAsync(
        IEventRegistrationRepository registrations,
        Guid eventId,
        string? bib,
        string? qrPayload,
        CancellationToken cancellationToken)
    {
        var trimmedBib = bib?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedBib))
        {
            return await registrations.GetByEventAndBibAsync(eventId, trimmedBib, cancellationToken)
                ?? throw new DomainException($"Không tìm thấy BIB {trimmedBib} trên giải này.");
        }

        if (string.IsNullOrWhiteSpace(qrPayload))
        {
            throw new DomainException("Cần bib hoặc qrPayload.");
        }

        try
        {
            using var doc = JsonDocument.Parse(qrPayload);
            var root = doc.RootElement;
            if (root.TryGetProperty("event_id", out var eventEl))
            {
                var qrEvent = eventEl.ValueKind == JsonValueKind.String
                    ? Guid.TryParse(eventEl.GetString(), out var g1) ? g1 : Guid.Empty
                    : eventEl.TryGetGuid(out var g2) ? g2 : Guid.Empty;
                if (qrEvent != Guid.Empty && qrEvent != eventId)
                {
                    throw new DomainException("QR không thuộc giải đang check-in.");
                }
            }

            if (root.TryGetProperty("bib", out var bibEl))
            {
                var fromQr = bibEl.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(fromQr))
                {
                    return await registrations.GetByEventAndBibAsync(eventId, fromQr, cancellationToken)
                        ?? throw new DomainException($"Không tìm thấy BIB {fromQr} trên giải này.");
                }
            }

            // Định danh bổ sung: user_id trên QR vé RaceDay
            if (root.TryGetProperty("user_id", out var userEl))
            {
                var userId = userEl.ValueKind == JsonValueKind.String
                    ? Guid.TryParse(userEl.GetString(), out var u1) ? u1 : Guid.Empty
                    : userEl.TryGetGuid(out var u2) ? u2 : Guid.Empty;
                if (userId != Guid.Empty)
                {
                    return await registrations.GetConfirmedByEventAndUserAsync(eventId, userId, cancellationToken)
                        ?? throw new DomainException("Không tìm thấy VĐV confirmed khớp QR (user_id).");
                }
            }
        }
        catch (DomainException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new DomainException("qrPayload không hợp lệ.");
        }

        throw new DomainException("QR thiếu số BIB / user_id.");
    }
}

public sealed record CheckInListItemDto(
    Guid RegistrationId,
    string BibNumber,
    string FullName,
    string Status,
    bool CheckedIn,
    DateTime? CheckedInAtUtc,
    string? DistanceLabel,
    double? DistanceKm,
    string RaceStatus = "not_started",
    string BibStatus = "normal",
    long Version = 0,
    DateTime? StartedAtUtc = null,
    DateTime? FinishedAtUtc = null,
    long? ResultSeconds = null);

public sealed record ListEventCheckInsQuery(Guid EventId)
    : IRequest<IReadOnlyList<CheckInListItemDto>>;

public sealed class ListEventCheckInsQueryHandler(
    IRaceEventRepository events,
    IEventRegistrationRepository registrations,
    IEventStaffMemberRepository staff,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListEventCheckInsQuery, IReadOnlyList<CheckInListItemDto>>
{
    public async Task<IReadOnlyList<CheckInListItemDto>> Handle(
        ListEventCheckInsQuery request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        if (request.EventId == Guid.Empty)
        {
            throw new DomainException("Thiếu eventId.");
        }

        var race = await events.GetByIdWithDetailsAsync(request.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        var allowed = currentUser.IsAdmin
            || race.OrganizerUserId == userId
            || await staff.ExistsActiveStaffUserAsync(request.EventId, userId, cancellationToken);
        if (!allowed)
        {
            throw new DomainException("Bạn không có quyền xem check-in giải này.");
        }

        var items = await registrations.ListByEventAsync(request.EventId, cancellationToken);
        return items
            .Where(r => r.Status == RegistrationStatus.Confirmed)
            .Select(r =>
            {
                string? distanceLabel = null;
                double? distanceKm = null;
                try
                {
                    var d = race.GetDistance(r.DistanceId);
                    distanceLabel = $"{d.Type} · {d.Name}";
                    distanceKm = d.DistanceKm;
                }
                catch
                {
                    /* ignore */
                }

                return new CheckInListItemDto(
                    r.Id,
                    r.BibNumber ?? string.Empty,
                    r.FullName,
                    RegistrationDtoMapper.ToApiStatus(r),
                    CheckedIn: r.CheckedInAtUtc is not null,
                    r.CheckedInAtUtc,
                    distanceLabel,
                    distanceKm,
                    RegistrationDtoMapper.ToApiRaceStatus(r.RaceStatus),
                    r.BibStatus.ToString().ToLowerInvariant(), r.Version,
                    r.StartedAtUtc, r.FinishedAtUtc, r.ResultSeconds);
            })
            .OrderByDescending(x => x.CheckedInAtUtc.HasValue)
            .ThenByDescending(x => x.CheckedInAtUtc)
            .ThenBy(x => x.BibNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
