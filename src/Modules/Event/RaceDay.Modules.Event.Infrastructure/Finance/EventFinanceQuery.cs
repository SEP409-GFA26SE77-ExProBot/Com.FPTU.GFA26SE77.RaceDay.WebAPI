using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Contracts.Finance;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Finance;

public sealed class EventFinanceQuery(EventDbContext db) : IEventFinanceQuery
{
    public async Task<EventFinanceInfo?> GetEventAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var race = await db.RaceEvents.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == eventId, cancellationToken);
        return race is null ? null : Map(race);
    }

    public async Task<IReadOnlyList<EventFinanceInfo>> ListByOrganizerAsync(
        Guid organizerUserId,
        CancellationToken cancellationToken = default)
    {
        var list = await db.RaceEvents.AsNoTracking()
            .Where(x => x.OrganizerUserId == organizerUserId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return list.Select(Map).ToList();
    }

    public async Task<RegistrationFinanceInfo?> GetRegistrationAsync(
        Guid registrationId,
        CancellationToken cancellationToken = default)
    {
        var reg = await db.Registrations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == registrationId, cancellationToken);
        if (reg is null)
        {
            return null;
        }

        var race = await db.RaceEvents.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == reg.EventId, cancellationToken);
        if (race is null)
        {
            return null;
        }

        return new RegistrationFinanceInfo(
            reg.Id,
            reg.EventId,
            reg.DistanceId,
            reg.UserId,
            StatusName(reg.Status),
            race.Name,
            race.OrganizerUserId,
            race.Status.ToString(),
            race.EventDate,
            race.StartTime);
    }

    public async Task<IReadOnlyList<Guid>> ListPaidEligibleRegistrationIdsAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        // PendingApproval / Confirmed / Rejected(đã trả rồi hoàn) — reconcile dùng payment.Paid làm nguồn thật.
        return await db.Registrations.AsNoTracking()
            .Where(x => x.EventId == eventId &&
                        (x.Status == RegistrationStatus.PendingApproval ||
                         x.Status == RegistrationStatus.Confirmed ||
                         x.Status == RegistrationStatus.Rejected ||
                         x.Status == RegistrationStatus.Cancelled))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    private static EventFinanceInfo Map(RaceEvent race) => new(
        race.Id,
        race.Name,
        race.OrganizerUserId,
        race.Status.ToString(),
        race.EventDate,
        race.StartTime,
        race.Organizer.AccountHolder,
        race.Organizer.AccountNumber,
        race.Organizer.BankName,
        race.Organizer.BankBranch,
        race.Organizer.TransferNote);

    private static string StatusName(RegistrationStatus s) => s switch
    {
        RegistrationStatus.PendingPayment => "pending_payment",
        RegistrationStatus.PendingApproval => "pending_approval",
        RegistrationStatus.Confirmed => "confirmed",
        RegistrationStatus.Rejected => "rejected",
        RegistrationStatus.Cancelled => "cancelled",
        _ => s.ToString().ToLowerInvariant()
    };
}
