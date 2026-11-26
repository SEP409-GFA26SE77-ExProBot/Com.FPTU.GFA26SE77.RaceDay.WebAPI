using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Application.Dashboard;
using RaceDay.Modules.Event.Application.Events;
using RaceDay.Modules.Event.Application.Registrations;
using RaceDay.Modules.Event.Domain.Complaints;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Modules.Event.Domain.Staff;
using RaceDay.Modules.Event.Infrastructure.Persistence;
using RaceDay.Shared.Application.Dashboard;

namespace RaceDay.Modules.Event.Infrastructure.Events;

public sealed class OrganizerDashboardReader(EventDbContext db) : IOrganizerDashboardReader
{
    public async Task<OrganizerDashboardDto> OverviewAsync(Guid userId, DateOnly today, CancellationToken ct)
    {
        var rows = await db.RaceEvents.AsNoTracking().Where(x => x.OrganizerUserId == userId)
            .Select(e => new { e.Id, e.Name, e.Status, e.EventDate,
                PendingRegistrations = db.Registrations.Count(r => r.EventId == e.Id && r.Status == RegistrationStatus.PendingApproval),
                PendingComplaints = db.Complaints.Count(c => c.EventId == e.Id && c.Status == ComplaintStatus.Pending)
            }).ToListAsync(ct);
        var work = rows.Select(e => new OrganizerEventWorkDto(e.Id, e.Name, RaceEventDtoMapper.ToApiStatus(e.Status),
            e.EventDate, e.PendingRegistrations, e.PendingComplaints)).ToArray();
        var upcomingIds = rows.Where(e => e.EventDate >= today &&
            (e.Status == RaceEventStatus.Approved || e.Status == RaceEventStatus.RegistrationOpen || e.Status == RaceEventStatus.Closed))
            .Select(e => e.Id).ToHashSet();
        return new(rows.Count,
            Enum.GetValues<RaceEventStatus>().Select(s => new DashboardCount(RaceEventDtoMapper.ToApiStatus(s), rows.Count(e => e.Status == s))).ToArray(),
            rows.Sum(e => e.PendingRegistrations), rows.Sum(e => e.PendingComplaints),
            work.Where(e => upcomingIds.Contains(e.EventId)).OrderBy(e => e.EventDate).ThenBy(e => e.EventId).Take(5).ToArray(),
            work.Where(e => e.PendingTasks > 0).OrderByDescending(e => e.PendingTasks).ThenBy(e => e.EventId).ToArray(), DateTime.UtcNow);
    }

    public async Task<EventDashboardDto?> EventAsync(Guid userId, Guid eventId, CancellationToken ct)
    {
        var race = await db.RaceEvents.AsNoTracking().Include(x => x.Distances).Include(x => x.Documents).AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == eventId && x.OrganizerUserId == userId, ct);
        if (race is null) return null;
        var regs = db.Registrations.AsNoTracking().Where(x => x.EventId == eventId);
        var statuses = await regs.GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        var eligible = regs.Where(x => x.Status == RegistrationStatus.Confirmed || x.Status == RegistrationStatus.Finished);
        var raceStatuses = await eligible.GroupBy(x => x.RaceStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        var checkedIn = await eligible.CountAsync(x => x.CheckedInAtUtc != null, ct);
        var byDistance = await eligible.GroupBy(x => x.DistanceId)
            .Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var complaints = await db.Complaints.CountAsync(x => x.EventId == eventId && x.Status == ComplaintStatus.Pending, ct);
        var staff = db.StaffMembers.AsNoTracking().Where(x => x.EventId == eventId && x.Status == EventStaffStatus.Active);
        var activeStaff = await staff.CountAsync(ct);
        var assignedStaff = await staff.CountAsync(x => x.StationId != null && x.StationId != "", ct);
        var requiredKeys = new[] { "official-letter", "security" }.Concat(race.Documents.Where(d => d.Required).Select(d => d.DocumentKey))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var missing = requiredKeys.Where(key => !race.Documents.Any(d => string.Equals(d.DocumentKey, key, StringComparison.OrdinalIgnoreCase)
            && (!string.IsNullOrWhiteSpace(d.FileName) || !string.IsNullOrWhiteSpace(d.FileUrl)))).ToArray();
        return new(eventId, statuses.Sum(x => x.Count),
            Enum.GetValues<RegistrationStatus>().Select(s => new DashboardCount(RegistrationDtoMapper.ToApiStatus(s),
                statuses.Where(x => x.Status == s).Sum(x => x.Count))).ToArray(),
            new(raceStatuses.Sum(x => x.Count), checkedIn),
            Enum.GetValues<RaceStatus>().Select(s => new DashboardCount(RegistrationDtoMapper.ToApiRaceStatus(s),
                raceStatuses.Where(x => x.Status == s).Sum(x => x.Count))).ToArray(), complaints,
            race.Distances.OrderBy(d => d.DistanceKm).ThenBy(d => d.Id).Select(d => new DistanceDashboardDto(d.Id, d.Name,
                d.Capacity, d.RegisteredCount, byDistance.GetValueOrDefault(d.Id))).ToArray(),
            new(race.Distances.Count, race.Distances.Count(d => d.HasBibRange), missing, 0, 0, activeStaff, assignedStaff), DateTime.UtcNow);
    }

    public async Task<RegistrationTrendDto?> TrendAsync(Guid userId, Guid eventId, DashboardDateRange range, CancellationToken ct)
    {
        if (!await db.RaceEvents.AnyAsync(x => x.Id == eventId && x.OrganizerUserId == userId, ct)) return null;
        var start = range.FromUtc;
        var end = range.ToUtcExclusive;
        var rows = await db.Registrations.AsNoTracking()
            .Where(x => x.EventId == eventId && x.CreatedAtUtc >= start && x.CreatedAtUtc < end)
            .GroupBy(x => new { x.DistanceId, Date = x.CreatedAtUtc.AddHours(7).Date })
            .Select(g => new { g.Key.DistanceId, g.Key.Date, Count = g.Count() }).ToListAsync(ct);
        var distances = await db.Distances.AsNoTracking().Where(x => x.EventId == eventId)
            .OrderBy(x => x.DistanceKm).ThenBy(x => x.Id).Select(x => new { x.Id, x.Name }).ToListAsync(ct);
        var totals = rows.GroupBy(x => DateOnly.FromDateTime(x.Date)).ToDictionary(g => g.Key, g => g.Sum(x => x.Count));
        var buckets = rows.ToDictionary(x => (x.DistanceId, DateOnly.FromDateTime(x.Date)), x => x.Count);
        return new(eventId, range.From, range.To, DashboardDateRange.TimeZone, rows.Sum(x => x.Count),
            range.Days().Select(d => new DailyCount(d, totals.GetValueOrDefault(d))).ToArray(),
            distances.Select(x => new DistanceRegistrationTrendDto(x.Id, x.Name,
                range.Days().Select(d => new DailyCount(d, buckets.GetValueOrDefault((x.Id, d)))).ToArray())).ToArray(), DateTime.UtcNow);
    }
}
