using System.Text.Json;
using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Domain.Staff;

public enum EventStaffRole
{
    /// <summary>Gộp Coordinator + Volunteer (DB value 0). Legacy Volunteer=2 được migrate về 0.</summary>
    RaceStaff = 0,
    Photographer = 1
}

public enum EventStaffStatus
{
    Invited = 0,
    Active = 1,
    Revoked = 2
}

/// <summary>Điểm / nhiệm vụ gán cho nhân sự (id ổn định từ FE map:… / duty:…).</summary>
public sealed record StaffStationAssignment(string StationId, string? StationLabel);

public sealed class EventStaffMember : AggregateRoot
{
    public const int MaxPhotographerStations = 5;
    public const int MaxRaceStaffStations = 1;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public Guid EventId { get; private set; }
    public EventStaffRole Role { get; private set; }
    public EventStaffStatus Status { get; private set; } = EventStaffStatus.Invited;
    public string FullName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public Guid? UserId { get; private set; }

    /// <summary>Primary (điểm đầu) — giữ để tương thích query cũ / SQL clear distance.</summary>
    public string? StationId { get; private set; }
    public string? StationLabel { get; private set; }

    /// <summary>jsonb: [{ "stationId", "stationLabel" }, …]. Photographer có thể nhiều; race-staff tối đa 1.</summary>
    public string StationsJson { get; private set; } = "[]";

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private EventStaffMember()
    {
    }

    public IReadOnlyList<StaffStationAssignment> GetStations()
    {
        var list = DeserializeStations(StationsJson);
        if (list.Count > 0)
        {
            return list;
        }

        if (!string.IsNullOrWhiteSpace(StationId))
        {
            return [new StaffStationAssignment(StationId, StationLabel)];
        }

        return [];
    }

    public static EventStaffMember Invite(
        Guid eventId,
        EventStaffRole role,
        string? email,
        string fullName,
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
        var member = new EventStaffMember
        {
            EventId = eventId,
            Role = role,
            Status = EventStaffStatus.Invited,
            FullName = fullName.Trim(),
            Email = normalizedEmail,
            Phone = NormalizePhone(phone),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        member.AssignStations(BuildInitial(stationId, stationLabel));
        return member;
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
        var member = new EventStaffMember
        {
            EventId = eventId,
            Role = role,
            Status = EventStaffStatus.Active,
            FullName = fullName.Trim(),
            Email = normalizedEmail,
            Phone = NormalizePhone(phone),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        member.AssignStations(BuildInitial(stationId, stationLabel));
        return member;
    }

    public void UpdateProfile(string fullName, string? phone, string? email = null)
    {
        if (Status == EventStaffStatus.Revoked)
        {
            throw new DomainException("Không sửa nhân sự đã gỡ.");
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new DomainException("Tên bắt buộc.");
        }

        FullName = fullName.Trim();
        Phone = NormalizePhone(phone);
        if (email is not null)
        {
            Email = string.IsNullOrWhiteSpace(email)
                ? $"local.{Guid.NewGuid():N}@staff.raceday.local"
                : NormalizeEmail(email);
        }

        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Gán 1 vị trí (race-staff) hoặc thay toàn bộ bằng 1 điểm.</summary>
    public void AssignStation(string? stationId, string? stationLabel)
    {
        AssignStations(BuildInitial(stationId, stationLabel));
    }

    /// <summary>
    /// Photographer: tối đa <see cref="MaxPhotographerStations"/> điểm.
    /// Race-staff: tối đa 1.
    /// </summary>
    public void AssignStations(IReadOnlyList<StaffStationAssignment>? stations)
    {
        if (Status == EventStaffStatus.Revoked)
        {
            throw new DomainException("Không gán vị trí cho nhân sự đã gỡ.");
        }

        var normalized = NormalizeStationList(stations);
        var max = Role == EventStaffRole.Photographer ? MaxPhotographerStations : MaxRaceStaffStations;
        if (normalized.Count > max)
        {
            throw new DomainException(
                Role == EventStaffRole.Photographer
                    ? $"Photographer tối đa {MaxPhotographerStations} điểm."
                    : "Nhân viên giải chỉ gán 1 vị trí.");
        }

        StationsJson = JsonSerializer.Serialize(normalized, JsonOpts);
        var primary = normalized.FirstOrDefault();
        StationId = primary?.StationId;
        StationLabel = primary?.StationLabel;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Gỡ mọi điểm map gắn cự ly đã xóa (map:{distanceId}:…).</summary>
    public bool RemoveStationsForDistance(Guid distanceId)
    {
        if (distanceId == Guid.Empty)
        {
            return false;
        }

        var prefix = $"map:{distanceId}:";
        var current = GetStations().ToList();
        var next = current
            .Where(s => !s.StationId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (next.Count == current.Count)
        {
            return false;
        }

        AssignStations(next);
        return true;
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

    private static List<StaffStationAssignment> BuildInitial(string? stationId, string? stationLabel)
    {
        var id = NormalizeStationId(stationId);
        if (id is null)
        {
            return [];
        }

        return
        [
            new StaffStationAssignment(
                id,
                string.IsNullOrWhiteSpace(stationLabel) ? null : stationLabel.Trim())
        ];
    }

    private static List<StaffStationAssignment> NormalizeStationList(
        IReadOnlyList<StaffStationAssignment>? stations)
    {
        if (stations is null || stations.Count == 0)
        {
            return [];
        }

        var result = new List<StaffStationAssignment>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in stations)
        {
            var id = NormalizeStationId(s.StationId);
            if (id is null || !seen.Add(id))
            {
                continue;
            }

            result.Add(new StaffStationAssignment(
                id,
                string.IsNullOrWhiteSpace(s.StationLabel) ? null : s.StationLabel.Trim()));
        }

        return result;
    }

    private static List<StaffStationAssignment> DeserializeStations(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
        {
            return [];
        }

        try
        {
            var items = JsonSerializer.Deserialize<List<StaffStationAssignmentDto>>(json, JsonOpts);
            if (items is null || items.Count == 0)
            {
                return [];
            }

            return NormalizeStationList(
                items
                    .Select(x => new StaffStationAssignment(x.StationId ?? "", x.StationLabel))
                    .ToList());
        }
        catch
        {
            return [];
        }
    }

    private static string NormalizeEmail(string? email)
        => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static string? NormalizePhone(string? phone)
        => string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();

    private static string? NormalizeStationId(string? stationId)
        => string.IsNullOrWhiteSpace(stationId) ? null : stationId.Trim();

    private sealed record StaffStationAssignmentDto(string? StationId, string? StationLabel);
}
