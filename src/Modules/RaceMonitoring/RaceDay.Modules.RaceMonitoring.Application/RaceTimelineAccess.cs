using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Application;

/// <summary>
/// Hai lối vào riêng. Cùng một quy tắc người được phép trong MVP, để RFID sau này chỉ thay lối ghi.
/// </summary>
internal static class RaceTimelineAccess
{
    public static Task EnsureCanViewAsync(
        Guid eventId,
        IEventOrganizerAccessQuery access,
        IEventSosOpsAccessQuery sosOps,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
        => EnsureAsync(
            eventId,
            access,
            sosOps,
            currentUser,
            "Chỉ chủ giải hoặc Coordinator Active mới xem timeline.",
            cancellationToken);

    public static Task EnsureCanRecordAsync(
        Guid eventId,
        IEventOrganizerAccessQuery access,
        IEventSosOpsAccessQuery sosOps,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
        => EnsureAsync(
            eventId,
            access,
            sosOps,
            currentUser,
            "Chỉ chủ giải hoặc Coordinator Active mới ghi nhận checkpoint.",
            cancellationToken);

    private static async Task EnsureAsync(
        Guid eventId,
        IEventOrganizerAccessQuery access,
        IEventSosOpsAccessQuery sosOps,
        ICurrentUserAccessor currentUser,
        string deniedMessage,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        _ = await access.GetAsync(eventId, cancellationToken) ?? throw new DomainException("Không tìm thấy giải.");
        if (!await sosOps.CanManageSosAsync(eventId, userId, cancellationToken))
        {
            throw new DomainException(deniedMessage);
        }
    }
}
