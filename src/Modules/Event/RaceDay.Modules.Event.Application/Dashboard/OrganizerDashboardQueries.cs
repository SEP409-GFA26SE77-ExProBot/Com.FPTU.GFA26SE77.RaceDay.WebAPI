using MediatR;
using RaceDay.Modules.Map.Contracts.Courses;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Application.Dashboard;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Dashboard;

public sealed record OrganizerEventWorkDto(Guid EventId, string Name, string Status, DateOnly EventDate,
    int PendingRegistrations, int PendingComplaints)
{
    public int PendingTasks => PendingRegistrations + PendingComplaints;
}
public sealed record OrganizerDashboardDto(int TotalEvents, IReadOnlyList<DashboardCount> EventsByStatus,
    int PendingRegistrations, int PendingComplaints, IReadOnlyList<OrganizerEventWorkDto> UpcomingEvents,
    IReadOnlyList<OrganizerEventWorkDto> EventsWithPendingTasks, DateTime GeneratedAtUtc);
public sealed record CheckInSummaryDto(int Eligible, int CheckedIn);
public sealed record DistanceDashboardDto(Guid DistanceId, string Name, int Capacity, int ReservedSlots,
    int ConfirmedRegistrations);
public sealed record PreparationDashboardDto(int TotalDistances, int DistancesWithBib,
    IReadOnlyList<string> MissingDocumentKeys, int CoursesReady, int CoursesWithCheckpoints,
    int ActiveStaff, int AssignedStaff);
public sealed record EventDashboardDto(Guid EventId, int TotalRegistrations,
    IReadOnlyList<DashboardCount> RegistrationsByStatus, CheckInSummaryDto CheckInSummary,
    IReadOnlyList<DashboardCount> RaceByStatus, int PendingComplaints,
    IReadOnlyList<DistanceDashboardDto> Distances, PreparationDashboardDto Preparation, DateTime GeneratedAtUtc);
public sealed record DistanceRegistrationTrendDto(Guid DistanceId, string Name, IReadOnlyList<DailyCount> Days);
public sealed record RegistrationTrendDto(Guid EventId, DateOnly From, DateOnly To, string TimeZone,
    int TotalRegistrations, IReadOnlyList<DailyCount> Days,
    IReadOnlyList<DistanceRegistrationTrendDto> Distances, DateTime GeneratedAtUtc);

public interface IOrganizerDashboardReader
{
    Task<OrganizerDashboardDto> OverviewAsync(Guid userId, DateOnly today, CancellationToken ct);
    Task<EventDashboardDto?> EventAsync(Guid userId, Guid eventId, CancellationToken ct);
    Task<RegistrationTrendDto?> TrendAsync(Guid userId, Guid eventId, DashboardDateRange range, CancellationToken ct);
}

public sealed record GetOrganizerDashboardQuery : IRequest<OrganizerDashboardDto>;
public sealed record GetEventDashboardQuery(Guid EventId) : IRequest<EventDashboardDto>;
public sealed record GetRegistrationTrendQuery(Guid EventId, DateOnly? From, DateOnly? To) : IRequest<RegistrationTrendDto>;

public sealed class GetOrganizerDashboardQueryHandler(IOrganizerDashboardReader reader, ICurrentUserAccessor user)
    : IRequestHandler<GetOrganizerDashboardQuery, OrganizerDashboardDto>
{
    public Task<OrganizerDashboardDto> Handle(GetOrganizerDashboardQuery request, CancellationToken ct)
        => reader.OverviewAsync(user.UserId ?? throw new DomainException("Cần đăng nhập."),
            DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)), ct);
}

public sealed class GetEventDashboardQueryHandler(IOrganizerDashboardReader reader,
    IEventCoursesReadQuery courses, ICurrentUserAccessor user) : IRequestHandler<GetEventDashboardQuery, EventDashboardDto>
{
    public async Task<EventDashboardDto> Handle(GetEventDashboardQuery request, CancellationToken ct)
    {
        var result = await reader.EventAsync(user.UserId ?? throw new DomainException("Cần đăng nhập."), request.EventId, ct)
            ?? throw new DomainException("Không tìm thấy giải thuộc quyền quản lý của bạn.");
        var distanceIds = result.Distances.Select(x => x.DistanceId).ToHashSet();
        var routes = (await courses.ListAsync(request.EventId, ct))
            .Where(x => distanceIds.Contains(x.DistanceId) && x.Polyline.Count >= 2).ToArray();
        return result with { Preparation = result.Preparation with {
            CoursesReady = routes.Select(x => x.DistanceId).Distinct().Count(),
            CoursesWithCheckpoints = routes.Where(x => x.Checkpoints.Count > 0).Select(x => x.DistanceId).Distinct().Count()
        }, GeneratedAtUtc = DateTime.UtcNow };
    }
}

public sealed class GetRegistrationTrendQueryHandler(IOrganizerDashboardReader reader, ICurrentUserAccessor user)
    : IRequestHandler<GetRegistrationTrendQuery, RegistrationTrendDto>
{
    public async Task<RegistrationTrendDto> Handle(GetRegistrationTrendQuery request, CancellationToken ct)
        => await reader.TrendAsync(user.UserId ?? throw new DomainException("Cần đăng nhập."), request.EventId,
            DashboardDateRange.Create(request.From, request.To, DateTime.UtcNow), ct)
            ?? throw new DomainException("Không tìm thấy giải thuộc quyền quản lý của bạn.");
}
