using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Contracts.Finance;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Infrastructure.Persistence;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Infrastructure.Finance;

public sealed class RegistrationCapacityPort(EventDbContext db) : IRegistrationCapacityPort
{
    public async Task ReleaseSlotAsync(Guid registrationId, CancellationToken cancellationToken = default)
    {
        var reg = await db.Registrations.FirstOrDefaultAsync(x => x.Id == registrationId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy đăng ký.");

        var race = await db.RaceEvents
            .Include(x => x.Distances)
            .FirstOrDefaultAsync(x => x.Id == reg.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        var distance = race.Distances.FirstOrDefault(d => d.Id == reg.DistanceId);
        distance?.DecrementRegistered();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkRegistrationCancelledAfterRefundAsync(
        Guid registrationId,
        CancellationToken cancellationToken = default)
    {
        var reg = await db.Registrations.FirstOrDefaultAsync(x => x.Id == registrationId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy đăng ký.");

        if (reg.Status == RegistrationStatus.Rejected)
        {
            // Organizer reject keeps Rejected; slot already released separately.
            return;
        }

        reg.MarkCancelledAfterRefund();
        await db.SaveChangesAsync(cancellationToken);
    }
}
