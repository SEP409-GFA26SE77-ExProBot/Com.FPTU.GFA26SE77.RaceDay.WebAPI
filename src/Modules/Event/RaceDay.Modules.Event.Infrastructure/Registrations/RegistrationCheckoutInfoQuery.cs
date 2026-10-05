using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Contracts.Registrations;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Registrations;

public sealed class RegistrationCheckoutInfoQuery(EventDbContext db) : IRegistrationCheckoutInfoQuery
{
    public async Task<RegistrationCheckoutInfo?> GetAsync(Guid registrationId, CancellationToken cancellationToken = default)
    {
        var reg = await db.Registrations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == registrationId, cancellationToken);
        if (reg is null)
        {
            return null;
        }

        var race = await db.RaceEvents.AsNoTracking()
            .Include(x => x.Distances)
            .FirstOrDefaultAsync(x => x.Id == reg.EventId, cancellationToken);
        if (race is null)
        {
            return null;
        }

        var distance = race.Distances.FirstOrDefault(d => d.Id == reg.DistanceId);
        if (distance is null)
        {
            return null;
        }

        var status = reg.Status switch
        {
            RegistrationStatus.PendingPayment => "pending_payment",
            RegistrationStatus.PendingApproval => "pending_approval",
            RegistrationStatus.Confirmed => "confirmed",
            RegistrationStatus.Rejected => "rejected",
            RegistrationStatus.Cancelled => "cancelled",
            _ => reg.Status.ToString().ToLowerInvariant()
        };

        return new RegistrationCheckoutInfo(
            reg.Id,
            reg.UserId,
            reg.EventId,
            reg.DistanceId,
            race.Name,
            distance.Type,
            distance.Name,
            distance.DistanceKm,
            distance.Price,
            string.IsNullOrWhiteSpace(distance.Currency) ? "VND" : distance.Currency,
            status);
    }
}
