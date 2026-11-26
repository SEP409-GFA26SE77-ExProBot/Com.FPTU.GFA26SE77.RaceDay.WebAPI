using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Iam.Application.Dashboard;
using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Modules.Iam.Infrastructure.Persistence;
using RaceDay.Shared.Application.Dashboard;

namespace RaceDay.Modules.Iam.Infrastructure.Repositories;

public sealed class AdminDashboardReader(IamDbContext db) : IAdminDashboardReader
{
    public async Task<AdminDashboardDto> ReadAsync(DashboardDateRange range, CancellationToken ct)
    {
        var statuses = await db.Users.AsNoTracking().GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        var roles = await (from u in db.Users.AsNoTracking()
                           join r in db.Roles on u.RoleId equals r.Id
                           group u by r.RoleName into g
                           orderby g.Key
                           select new UsersByRoleDto(g.Key, g.Count())).ToListAsync(ct);
        var start = range.FromUtc;
        var end = range.ToUtcExclusive;
        var daily = await db.Users.AsNoTracking().Where(x => x.CreatedAt >= start && x.CreatedAt < end)
            .GroupBy(x => x.CreatedAt.AddHours(7).Date)
            .Select(g => new { Date = g.Key, Count = g.Count() }).ToListAsync(ct);
        var counts = daily.ToDictionary(x => DateOnly.FromDateTime(x.Date), x => x.Count);
        int Count(UserStatus status) => statuses.Where(x => x.Status == status).Sum(x => x.Count);
        return new(statuses.Sum(x => x.Count), Count(UserStatus.Active), Count(UserStatus.Pending),
            Count(UserStatus.Locked), daily.Sum(x => x.Count), roles,
            range.Days().Select(d => new DailyCount(d, counts.GetValueOrDefault(d))).ToArray(),
            range.From, range.To, DashboardDateRange.TimeZone, DateTime.UtcNow);
    }
}
