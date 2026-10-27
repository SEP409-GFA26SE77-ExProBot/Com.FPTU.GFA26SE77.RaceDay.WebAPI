using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Modules.Event.Domain.Staff;
using RaceDay.Modules.Event.Domain.Supplies;
using RaceDay.Modules.Event.Infrastructure.Persistence;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Infrastructure.Repositories;

public sealed class RaceEventRepository(EventDbContext db) : IRaceEventRepository
{
    public Task<RaceEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.RaceEvents.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<RaceEvent?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
        => db.RaceEvents
            .Include(x => x.Distances).ThenInclude(d => d.Checkpoints)
            .Include(x => x.Documents)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<RaceEvent>> ListByStatusAsync(RaceEventStatus status, CancellationToken cancellationToken = default)
        => await db.RaceEvents
            .Include(x => x.Distances).ThenInclude(d => d.Checkpoints)
            .Include(x => x.Documents)
            .Where(x => x.Status == status)
            .OrderByDescending(x => x.SubmittedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RaceEvent>> ListPublicAsync(CancellationToken cancellationToken = default)
        => await db.RaceEvents
            .Include(x => x.Distances)
            .Where(x => x.Status == RaceEventStatus.Approved
                        || x.Status == RaceEventStatus.RegistrationOpen
                        || x.Status == RaceEventStatus.Closed
                        || x.Status == RaceEventStatus.Completed)
            .OrderBy(x => x.EventDate)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RaceEvent>> ListByOrganizerAsync(Guid organizerUserId, CancellationToken cancellationToken = default)
        => await db.RaceEvents
            .Include(x => x.Distances)
            .Where(x => x.OrganizerUserId == organizerUserId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(RaceEvent raceEvent, CancellationToken cancellationToken = default)
        => await db.RaceEvents.AddAsync(raceEvent, cancellationToken);

    /// <summary>
    /// Chỉ attach khi entity detached. Entity đã tracked (Include) thì bỏ qua —
    /// gọi <see cref="DbSet{T}.Update"/> sẽ đánh dấu children mới (có Guid sẵn) thành
    /// Modified thay vì Added → DbUpdateConcurrencyException khi ReplaceDistances.
    /// </summary>
    public void Update(RaceEvent raceEvent)
    {
        var entry = db.Entry(raceEvent);
        if (entry.State == EntityState.Detached)
        {
            db.RaceEvents.Update(raceEvent);
        }
    }

    public async Task PersistDistanceReplaceAsync(
        Guid eventId,
        IReadOnlyList<EventDistance> distances,
        string? ticketPricingJson,
        CancellationToken cancellationToken = default)
    {
        // Clear tracker — tránh concurrency owned Start/Finish từ Include trước đó.
        db.ChangeTracker.Clear();

        var existing = await db.Distances
            .Where(d => d.EventId == eventId)
            .ToListAsync(cancellationToken);
        var existingById = existing.ToDictionary(d => d.Id);
        var incomingIds = distances.Select(d => d.Id).ToHashSet();

        var removeIds = existing
            .Where(d => !incomingIds.Contains(d.Id))
            .Select(d => d.Id)
            .ToList();

        if (removeIds.Count > 0)
        {
            var blocked = await db.Registrations
                .Where(r => removeIds.Contains(r.DistanceId))
                .Select(r => r.DistanceId)
                .Distinct()
                .ToListAsync(cancellationToken);
            if (blocked.Count > 0)
            {
                throw new DomainException(
                    "Không thể xóa cự ly đã có vận động viên đăng ký. Hãy giữ cự ly hoặc chuyển ĐK trước.");
            }

            await db.Checkpoints
                .Where(c => removeIds.Contains(c.DistanceId))
                .ExecuteDeleteAsync(cancellationToken);

            await db.Distances
                .Where(d => removeIds.Contains(d.Id))
                .ExecuteDeleteAsync(cancellationToken);

            // Map courses + staff station gắn distanceId cũ (cùng Postgres, không FK).
            foreach (var distId in removeIds)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""DELETE FROM map.race_courses WHERE "DistanceId" = {distId}""",
                    cancellationToken);

                var prefix = $"map:{distId}:%";
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE event.event_staff_members
                    SET "StationId" = NULL, "StationLabel" = NULL
                    WHERE "EventId" = {eventId}
                      AND "StationId" LIKE {prefix}
                    """,
                    cancellationToken);
            }
        }

        foreach (var incoming in distances)
        {
            if (existingById.TryGetValue(incoming.Id, out var current))
            {
                current.UpdateDetails(
                    incoming.Name,
                    incoming.DistanceKm,
                    incoming.Capacity,
                    incoming.Price,
                    incoming.StartAt,
                    incoming.EndAt,
                    incoming.Type,
                    incoming.Color);
            }
            else
            {
                db.Distances.Add(incoming);
            }
        }

        if (ticketPricingJson is not null)
        {
            var updated = await db.RaceEvents
                .Where(e => e.Id == eventId)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(e => e.TicketPricingJson, ticketPricingJson)
                        .SetProperty(e => e.UpdatedAtUtc, DateTime.UtcNow),
                    cancellationToken);

            if (updated == 0)
            {
                throw new DomainException("Không tìm thấy giải khi cập nhật giá vé.");
            }
        }
        else
        {
            await db.RaceEvents
                .Where(e => e.Id == eventId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(e => e.UpdatedAtUtc, DateTime.UtcNow),
                    cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task PersistDocumentsReplaceAsync(
        Guid eventId,
        IReadOnlyList<EventDocument> documents,
        CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();

        await db.Documents
            .Where(d => d.EventId == eventId)
            .ExecuteDeleteAsync(cancellationToken);

        if (documents.Count > 0)
        {
            db.Documents.AddRange(documents);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

public sealed class EventRegistrationRepository(EventDbContext db) : IEventRegistrationRepository
{
    public Task<EventRegistration?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.Registrations.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<EventRegistration>> ListByEventAsync(Guid eventId, CancellationToken cancellationToken = default)
        => await db.Registrations
            .Where(x => x.EventId == eventId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EventRegistration>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await db.Registrations
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsForUserDistanceAsync(Guid eventId, Guid distanceId, Guid userId, CancellationToken cancellationToken = default)
        => db.Registrations.AnyAsync(
            x => x.EventId == eventId && x.DistanceId == distanceId && x.UserId == userId
                 && x.Status != RegistrationStatus.Cancelled && x.Status != RegistrationStatus.Rejected,
            cancellationToken);

    public async Task<IReadOnlyList<EventRegistration>> ListActiveByUserEventAsync(
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken = default)
        => await db.Registrations
            .Where(x => x.EventId == eventId
                        && x.UserId == userId
                        && x.Status != RegistrationStatus.Cancelled
                        && x.Status != RegistrationStatus.Rejected)
            .ToListAsync(cancellationToken);

    public async Task<string> NextBibNumberAsync(
        Guid eventId,
        Guid distanceId,
        CancellationToken cancellationToken = default)
    {
        var distance = await db.Distances.AsNoTracking()
            .Where(x => x.EventId == eventId && x.Id == distanceId)
            .Select(x => new { x.Name, x.BibPrefix, x.BibRangeStart, x.BibRangeEnd })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new RaceDay.Shared.Domain.Exceptions.DomainException("Không tìm thấy cự ly.");

        if (distance.BibRangeStart is not >= 1 || distance.BibRangeEnd is not >= 1
            || distance.BibRangeEnd < distance.BibRangeStart)
        {
            throw new RaceDay.Shared.Domain.Exceptions.DomainException(
                $"Cự ly «{distance.Name}» chưa cấu hình dải BIB.");
        }

        var prefix = (distance.BibPrefix ?? string.Empty).Trim();
        var rangeStart = distance.BibRangeStart.Value;
        var rangeEnd = distance.BibRangeEnd.Value;

        var bibs = await db.Registrations
            .Where(x => x.EventId == eventId && x.BibNumber != null)
            .Select(x => x.BibNumber!)
            .ToListAsync(cancellationToken);

        var used = new HashSet<int>();
        foreach (var b in bibs)
        {
            if (TryParseBibSequence(b, prefix, out var n)
                && n >= rangeStart
                && n <= rangeEnd)
            {
                used.Add(n);
            }
        }

        for (var n = rangeStart; n <= rangeEnd; n++)
        {
            if (used.Contains(n))
            {
                continue;
            }

            var candidate = EventDistance.FormatBib(prefix, n);
            if (bibs.Any(b => string.Equals(b, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            return candidate;
        }

        throw new RaceDay.Shared.Domain.Exceptions.DomainException(
            string.IsNullOrEmpty(prefix)
                ? $"Đã hết số BIB trong dải {rangeStart:D4}–{rangeEnd:D4}."
                : $"Đã hết số BIB trong dải {prefix}{rangeStart:D4}–{prefix}{rangeEnd:D4}.");
    }

    private static bool TryParseBibSequence(string bib, string prefix, out int sequence)
    {
        sequence = 0;
        var raw = (bib ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return false;
        }

        if (string.IsNullOrEmpty(prefix))
        {
            return int.TryParse(raw, out sequence);
        }

        if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(raw[prefix.Length..], out sequence);
    }

    public async Task<EventRegistration?> GetByEventAndBibAsync(
        Guid eventId,
        string bibNumber,
        CancellationToken cancellationToken = default)
    {
        var bib = (bibNumber ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(bib))
        {
            return null;
        }

        return await db.Registrations.FirstOrDefaultAsync(
            x => x.EventId == eventId && x.BibNumber == bib,
            cancellationToken);
    }

    public async Task<EventRegistration?> GetConfirmedByEventAndUserAsync(
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken = default)
        => await db.Registrations.FirstOrDefaultAsync(
            x => x.EventId == eventId
                 && x.UserId == userId
                 && (x.Status == RegistrationStatus.Confirmed || x.Status == RegistrationStatus.Finished),
            cancellationToken);

    public async Task AddAsync(EventRegistration registration, CancellationToken cancellationToken = default)
        => await db.Registrations.AddAsync(registration, cancellationToken);

    public Task<EventRegistration?> GetFinishedByEventAndUserAsync(
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken = default)
        => db.Registrations.AsNoTracking()
            .Where(x => x.EventId == eventId && x.UserId == userId
                && x.Status == RegistrationStatus.Finished && x.FinishedAtUtc != null)
            .OrderByDescending(x => x.FinishedAtUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public void Update(EventRegistration registration) => db.Registrations.Update(registration);
}

public sealed class RegistrationCheckpointPassageRepository(EventDbContext db) : IRegistrationCheckpointPassageRepository
{
    public Task<RegistrationCheckpointPassage?> GetByRegistrationCheckpointAsync(
        Guid registrationId,
        Guid checkpointId,
        CancellationToken cancellationToken = default)
        => db.RegistrationCheckpointPassages.FirstOrDefaultAsync(
            x => x.RegistrationId == registrationId && x.CheckpointId == checkpointId,
            cancellationToken);

    public async Task AddAsync(RegistrationCheckpointPassage passage, CancellationToken cancellationToken = default)
        => await db.RegistrationCheckpointPassages.AddAsync(passage, cancellationToken);
}

public sealed class EventStaffMemberRepository(EventDbContext db) : IEventStaffMemberRepository
{
    public Task<EventStaffMember?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.StaffMembers.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<EventStaffMember>> ListByEventAsync(
        Guid eventId,
        EventStaffRole? role = null,
        CancellationToken cancellationToken = default)
    {
        var q = db.StaffMembers.AsNoTracking().Where(x => x.EventId == eventId);
        if (role is not null)
        {
            q = q.Where(x => x.Role == role);
        }

        return await q.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsActiveEmailRoleAsync(
        Guid eventId,
        EventStaffRole role,
        string email,
        CancellationToken cancellationToken = default)
        => db.StaffMembers.AnyAsync(
            x => x.EventId == eventId
                 && x.Role == role
                 && x.Email == email
                 && x.Status != EventStaffStatus.Revoked,
            cancellationToken);

    public Task<bool> ExistsActiveStaffUserAsync(
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken = default)
        => db.StaffMembers.AnyAsync(
            x => x.EventId == eventId
                 && x.UserId == userId
                 && x.Status == EventStaffStatus.Active,
            cancellationToken);

    public async Task AddAsync(EventStaffMember member, CancellationToken cancellationToken = default)
        => await db.StaffMembers.AddAsync(member, cancellationToken);

    public void Update(EventStaffMember member) => db.StaffMembers.Update(member);

    public async Task<IReadOnlyList<EventStaffMember>> ListInvitedByEmailOrUserAsync(
        string email,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return await db.StaffMembers
            .Where(x =>
                x.Status == EventStaffStatus.Invited
                && (x.UserId == userId || x.Email == normalized))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EventStaffMember>> ListActiveByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
        => await db.StaffMembers
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.Status == EventStaffStatus.Active)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToListAsync(cancellationToken);
}

public sealed class EventSupplyItemRepository(EventDbContext db) : IEventSupplyItemRepository
{
    public Task<EventSupplyItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.SupplyItems.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<EventSupplyItem>> ListByEventAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
        => await db.SupplyItems
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(EventSupplyItem item, CancellationToken cancellationToken = default)
        => await db.SupplyItems.AddAsync(item, cancellationToken);

    public void Update(EventSupplyItem item) => db.SupplyItems.Update(item);

    public void Remove(EventSupplyItem item) => db.SupplyItems.Remove(item);

    public async Task RemoveAllForEventAsync(Guid eventId, CancellationToken cancellationToken = default)
        => await db.SupplyItems.Where(x => x.EventId == eventId).ExecuteDeleteAsync(cancellationToken);
}
