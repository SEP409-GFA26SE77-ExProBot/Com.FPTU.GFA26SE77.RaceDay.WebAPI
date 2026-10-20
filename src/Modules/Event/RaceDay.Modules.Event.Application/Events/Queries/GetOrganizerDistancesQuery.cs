using System.Text.Json;
using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Events;

/// <summary>
/// Organizer setup: cự ly + ticketPricing (không lẫn public filter distances).
/// </summary>
public sealed record OrganizerDistanceItemDto(
    Guid Id,
    string Name,
    double DistanceKm,
    int Capacity,
    int RegisteredCount,
    decimal Price,
    string Currency,
    TimeOnly? StartAt,
    TimeOnly? EndAt);

public sealed record OrganizerDistancesDetailDto(
    Guid EventId,
    string Status,
    IReadOnlyList<OrganizerDistanceItemDto> Distances,
    JsonElement? TicketPricing);

public sealed record GetOrganizerDistancesQuery(Guid EventId)
    : IRequest<OrganizerDistancesDetailDto>;

public sealed class GetOrganizerDistancesQueryHandler(
    IRaceEventRepository events,
    ICurrentUserAccessor currentUser)
    : IRequestHandler<GetOrganizerDistancesQuery, OrganizerDistancesDetailDto>
{
    public async Task<OrganizerDistancesDetailDto> Handle(
        GetOrganizerDistancesQuery request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var race = await events.GetByIdWithDetailsAsync(request.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        if (!currentUser.IsAdmin && race.OrganizerUserId != userId)
        {
            throw new DomainException("Bạn không phải chủ giải này.");
        }

        JsonElement? ticketPricing = null;
        if (!string.IsNullOrWhiteSpace(race.TicketPricingJson) && race.TicketPricingJson is not "{}")
        {
            using var doc = JsonDocument.Parse(race.TicketPricingJson);
            ticketPricing = doc.RootElement.Clone();
        }

        var distances = race.Distances
            .OrderBy(d => d.DistanceKm)
            .Select(d => new OrganizerDistanceItemDto(
                d.Id,
                string.IsNullOrWhiteSpace(d.Name) ? d.Type : d.Name,
                d.DistanceKm,
                d.Capacity,
                d.RegisteredCount,
                d.Price,
                d.Currency,
                d.StartAt,
                d.EndAt))
            .ToList();

        return new OrganizerDistancesDetailDto(
            race.Id,
            RaceEventDtoMapper.ToApiStatus(race.Status),
            distances,
            ticketPricing);
    }
}
