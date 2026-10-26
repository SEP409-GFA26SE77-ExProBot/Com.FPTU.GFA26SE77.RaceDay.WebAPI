using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Domain.Staff;

public enum EventStaffRole
{
    Coordinator = 0,
    Photographer = 1,
    Volunteer = 2
}

public enum EventStaffStatus
{
    Invited = 0,
    Active = 1,
    Revoked = 2
}

public sealed class EventStaffMember : AggregateRoot
{
    public Guid EventId { get; private set; }
    public EventStaffRole Role { get; private set; }
    public EventStaffStatus Status { get; private set; } = EventStaffStatus.Invited;
    public string FullName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public Guid? UserId { get; private set; }
    public string? StationId { get; private set; }
    public string? StationLabel { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private EventStaffMember()
    {
    }

    public static EventStaffMember Invite(
        Guid eventId,
        EventStaffRole role,
        string email,
        string fullName,
        string? phone,
        string? stationId,
        string? stationLabel)
    {
        if (eventId == Guid.Empty)
        {
            throw new DomainException("Event không hợp lệ.");
        }

        var normalizedEmail = NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            throw new DomainException("Email bắt buộc khi mời.");
        }

        var name = string.IsNullOrWhiteSpace(fullName)
            ? normalizedEmail.Split('@')[0]
            : fullName.Trim();

        var now = DateTime.UtcNow;
        return new EventStaffMember
        {
            EventId = eventId,
            Role = role,
            Status = EventStaffStatus.Invited,
            FullName = name,
            Email = normalizedEmail,
            Phone = NormalizePhone(phone),
            StationId = NormalizeStationId(stationId),
            StationLabel = string.IsNullOrWhiteSpace(stationLabel) ? null : stationLabel.Trim(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    public static EventStaffMember AddDirect(
        Guid eventId,
        EventStaffRole role,
        string fullName,
        string? email,
        string? phone,
        string? stationId,
        string? stationLabel)
    {
        if (eventId == Guid.Empty)
        {
            throw new DomainException("Event không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new DomainException("Tên bắt buộc.");
        }

        var normalizedEmail = string.IsNullOrWhiteSpace(email)
            ? $"local.{Guid.NewGuid():N}@staff.raceday.local"
            : NormalizeEmail(email);

        var now = DateTime.UtcNow;
        return new EventStaffMember
        {
            EventId = eventId,
            Role = role,
            Status = EventStaffStatus.Active,
            FullName = fullName.Trim(),
            Email = normalizedEmail,
            Phone = NormalizePhone(phone),
            StationId = NormalizeStationId(stationId),
            StationLabel = string.IsNullOrWhiteSpace(stationLabel) ? null : stationLabel.Trim(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    public void AssignStation(string? stationId, string? stationLabel)
    {
        if (Status == EventStaffStatus.Revoked)
        {
            throw new DomainException("Không gán vị trí cho nhân sự đã gỡ.");
        }

        StationId = NormalizeStationId(stationId);
        StationLabel = string.IsNullOrWhiteSpace(stationLabel) ? null : stationLabel.Trim();
        if (StationId is null)
        {
            StationLabel = null;
        }

        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Revoke()
    {
        if (Status == EventStaffStatus.Revoked)
        {
            return;
        }

        Status = EventStaffStatus.Revoked;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void LinkUser(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            return;
        }

        UserId = userId;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Kích hoạt sau lần đăng nhập đầu (khác đăng ký public).</summary>
    public void Activate(Guid? userId = null)
    {
        if (Status == EventStaffStatus.Revoked)
        {
            throw new DomainException("Không kích hoạt nhân sự đã gỡ.");
        }

        if (userId is { } uid && uid != Guid.Empty)
        {
            UserId = uid;
        }

        Status = EventStaffStatus.Active;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string NormalizeEmail(string? email)
        => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static string? NormalizePhone(string? phone)
        => string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();

    private static string? NormalizeStationId(string? stationId)
        => string.IsNullOrWhiteSpace(stationId) ? null : stationId.Trim();
}
