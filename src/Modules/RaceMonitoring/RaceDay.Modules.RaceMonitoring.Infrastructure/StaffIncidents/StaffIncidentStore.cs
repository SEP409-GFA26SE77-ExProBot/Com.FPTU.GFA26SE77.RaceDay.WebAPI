using Microsoft.EntityFrameworkCore;
using Npgsql;
using RaceDay.Modules.RaceMonitoring.Contracts.StaffIncidents;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.StaffMonitoring;
using RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence;
using RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence.Configurations.StaffMonitoring;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.StaffIncidents;

public sealed class StaffIncidentStore(RaceMonitoringDbContext db) : IStaffIncidentSink, IStaffIncidentQuery
{
    public const int MaxPageSize = 100;

    public async Task RecordLeftZoneAsync(
        StaffIncidentStaff staff,
        StaffIncidentPoint location,
        DateTimeOffset leftAtUtc,
        CancellationToken cancellationToken = default)
    {
        var incident = StaffIncident.LeftZone(
            Author(staff),
            new StaffIncidentLocation(location.Latitude, location.Longitude, location.AccuracyMeters),
            leftAtUtc.UtcDateTime);
        db.StaffIncidents.Add(incident);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsOpenExitConflict(ex))
        {
            // Already outside: the tracker forgot the state (no GPS for a while) and saw the exit again.
            db.Entry(incident).State = EntityState.Detached;
        }
    }

    public Task RecordReturnedAsync(
        Guid eventId,
        Guid userId,
        DateTimeOffset returnedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var returnedAt = returnedAtUtc.UtcDateTime;
        return db.StaffIncidents
            .Where(x => x.EventId == eventId
                && x.UserId == userId
                && x.Kind == StaffIncidentKind.OutOfZone
                && x.ReturnedAtUtc == null
                && x.OccurredAtUtc <= returnedAt)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.ReturnedAtUtc, returnedAt), cancellationToken);
    }

    public async Task<StaffIncidentItem> RecordReportAsync(
        StaffReportInput report,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<ContingencyIncidentType>(report.IncidentType, ignoreCase: false, out var type)
            || !StaffIncidentTypes.All.Contains(report.IncidentType))
        {
            throw new DomainException("Loại sự cố không hợp lệ.");
        }

        var location = report.Location is { } point
            ? new StaffIncidentLocation(point.Latitude, point.Longitude, point.AccuracyMeters)
            : null;
        var incident = StaffIncident.Report(
            Author(report.Staff),
            type,
            report.Description,
            report.PhotoUrl,
            location,
            report.ReportedAtUtc.UtcDateTime);
        db.StaffIncidents.Add(incident);
        await db.SaveChangesAsync(cancellationToken);
        return ToItem(incident);
    }

    public async Task<StaffIncidentPage> ListAsync(StaffIncidentFilter filter, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);
        var all = db.StaffIncidents.AsNoTracking().Where(x => x.EventId == filter.EventId);

        var counts = await all
            .GroupBy(_ => 1)
            .Select(g => new
            {
                OutOfZone = g.Count(x => x.Kind == StaffIncidentKind.OutOfZone),
                Reports = g.Count(x => x.Kind == StaffIncidentKind.Report),
                Outside = g.Count(x => x.Kind == StaffIncidentKind.OutOfZone && x.ReturnedAtUtc == null)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var query = all;
        if (filter.Kind is { } kindName)
        {
            if (!Enum.TryParse<StaffIncidentKind>(kindName, ignoreCase: true, out var kind))
            {
                throw new DomainException("Loại sự cố nhân viên không hợp lệ (OutOfZone hoặc Report).");
            }

            query = query.Where(x => x.Kind == kind);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = "%" + EscapeLike(filter.Search.Trim()) + "%";
            query = query.Where(x =>
                EF.Functions.ILike(x.StaffName, pattern, "\\")
                || EF.Functions.ILike(x.Role, pattern, "\\")
                || x.ZoneName != null && EF.Functions.ILike(x.ZoneName, pattern, "\\")
                || x.Description != null && EF.Functions.ILike(x.Description, pattern, "\\"));
        }

        var total = filter.Kind is null && string.IsNullOrWhiteSpace(filter.Search)
            ? (counts?.OutOfZone ?? 0) + (counts?.Reports ?? 0)
            : await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new StaffIncidentPage(
            rows.Select(ToItem).ToArray(),
            total,
            page,
            pageSize,
            counts?.OutOfZone ?? 0,
            counts?.Reports ?? 0,
            counts?.Outside ?? 0);
    }

    private static StaffIncidentAuthor Author(StaffIncidentStaff staff)
        => new(staff.EventId, staff.UserId, staff.Name, staff.Role, staff.ZoneName);

    private static StaffIncidentItem ToItem(StaffIncident x)
        => new(
            x.Id,
            x.Kind.ToString(),
            x.UserId,
            x.StaffName,
            x.Role,
            x.ZoneName,
            x.IncidentType?.ToString(),
            x.Description,
            x.PhotoUrl,
            x.Latitude,
            x.Longitude,
            x.AccuracyMeters,
            Utc(x.OccurredAtUtc),
            x.ReturnedAtUtc is { } returned ? Utc(returned) : null);

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static bool IsOpenExitConflict(DbUpdateException ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
                && postgres.ConstraintName == StaffIncidentConfiguration.OpenExitIndex)
            {
                return true;
            }
        }

        return false;
    }

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
