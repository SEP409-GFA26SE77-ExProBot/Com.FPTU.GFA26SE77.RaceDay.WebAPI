using MediatR;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Application.Dashboard;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.Application.Dashboard;

public sealed record UsersByRoleDto(string Role, int Count);
public sealed record AdminDashboardDto(
    int TotalUsers, int ActiveUsers, int PendingUsers, int LockedUsers,
    int NewUsersInPeriod, IReadOnlyList<UsersByRoleDto> UsersByRole,
    IReadOnlyList<DailyCount> NewUsersByDay, DateOnly From, DateOnly To,
    string TimeZone, DateTime GeneratedAtUtc);

public interface IAdminDashboardReader
{
    Task<AdminDashboardDto> ReadAsync(DashboardDateRange range, CancellationToken ct);
}

public sealed record GetAdminDashboardQuery(DateOnly? From, DateOnly? To) : IRequest<AdminDashboardDto>;

public sealed class GetAdminDashboardQueryHandler(IAdminDashboardReader reader, ICurrentUserAccessor user)
    : IRequestHandler<GetAdminDashboardQuery, AdminDashboardDto>
{
    public Task<AdminDashboardDto> Handle(GetAdminDashboardQuery request, CancellationToken ct)
    {
        if (user.UserId is null || !user.IsAdmin) throw new DomainException("Chỉ Admin được xem dashboard tài khoản.");
        return reader.ReadAsync(DashboardDateRange.Create(request.From, request.To, DateTime.UtcNow), ct);
    }
}
