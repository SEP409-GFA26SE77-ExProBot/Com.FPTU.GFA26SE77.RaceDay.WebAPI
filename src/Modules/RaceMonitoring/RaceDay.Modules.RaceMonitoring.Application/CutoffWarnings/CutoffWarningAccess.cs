using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Application.CutoffWarnings;

/// <summary>
/// Admin sees every event. Otherwise the same rule as the SOS dashboard:
/// event organizer or Active Race-staff on that event.
/// </summary>
public sealed class CutoffWarningAccess(
    IEventOrganizerAccessQuery events,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser)
{
    public CutoffWarningViewer CurrentViewer()
        => new(
            currentUser.UserId ?? throw new DomainException("Cần đăng nhập."),
            currentUser.IsAdmin);

    public async Task<bool> CanManageAsync(Guid eventId, CutoffWarningViewer viewer, CancellationToken cancellationToken)
        => viewer.IsAdmin || await sosOps.CanManageSosAsync(eventId, viewer.UserId, cancellationToken);

    public async Task EnsureCanManageAsync(Guid eventId, CutoffWarningViewer viewer, CancellationToken cancellationToken)
    {
        _ = await events.GetAsync(eventId, cancellationToken) ?? throw new DomainException("Không tìm thấy giải.");
        if (!await CanManageAsync(eventId, viewer, cancellationToken))
        {
            throw new DomainException("Chỉ Admin, chủ giải hoặc Race-staff Active mới xem cảnh báo cut-off của giải.");
        }
    }
}

public sealed record CutoffWarningViewer(Guid UserId, bool IsAdmin);
