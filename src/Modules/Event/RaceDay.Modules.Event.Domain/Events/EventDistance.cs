using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Domain.Events;

/// <summary>One checkpoint as drawn on the organizer's map course.</summary>
public sealed record CourseCheckpointDraft(
    string ExternalId,
    string Name,
    double Km,
    double Lat,
    double Lng,
    CheckpointType Type,
    string? Cutoff);

/// <summary><see cref="NewCheckpoints"/> are not tracked yet; the repository must add them explicitly.</summary>
public sealed record CheckpointSyncOutcome(int Updated, int Removed, IReadOnlyList<EventCheckpoint> NewCheckpoints)
{
    public int Added => NewCheckpoints.Count;
}

public sealed class EventCheckpoint : Entity
{
    public Guid DistanceId { get; private set; }
    public string ExternalId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public double Km { get; private set; }
    public double Lat { get; private set; }
    public double Lng { get; private set; }
    public CheckpointType Type { get; private set; }
    public string? Cutoff { get; private set; }
    public int? Elevation { get; private set; }
    public int SortOrder { get; private set; }

    private EventCheckpoint()
    {
    }

    public static EventCheckpoint Create(
        Guid distanceId,
        string externalId,
        string name,
        double km,
        double lat,
        double lng,
        CheckpointType type,
        string? cutoff,
        int? elevation,
        int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Tên checkpoint không được để trống.");
        }

        return new EventCheckpoint
        {
            DistanceId = distanceId,
            ExternalId = string.IsNullOrWhiteSpace(externalId) ? Guid.NewGuid().ToString("N")[..12] : externalId.Trim(),
            Name = name.Trim(),
            Km = km,
            Lat = lat,
            Lng = lng,
            Type = type,
            Cutoff = cutoff?.Trim(),
            Elevation = elevation,
            SortOrder = sortOrder
        };
    }

    /// <summary>Applies the map course values. A null cut-off keeps the stored one; elevation is kept.</summary>
    public void UpdateFromCourse(CourseCheckpointDraft draft, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            throw new DomainException("Tên checkpoint không được để trống.");
        }

        ExternalId = draft.ExternalId.Trim();
        Name = draft.Name.Trim();
        Km = draft.Km;
        Lat = draft.Lat;
        Lng = draft.Lng;
        Type = draft.Type;
        if (!string.IsNullOrWhiteSpace(draft.Cutoff))
        {
            Cutoff = draft.Cutoff.Trim();
        }

        SortOrder = sortOrder;
    }
}

public sealed class EventDistance : Entity
{
    private readonly List<EventCheckpoint> _checkpoints = [];

    public Guid EventId { get; private set; }
    /// <summary>Mã chip BXH (3KM, 5KM, HM, M, OTHER…). Không dùng cho map — map theo DistanceKm.</summary>
    public string Type { get; private set; } = "OTHER";
    public string Name { get; private set; } = string.Empty;
    public double DistanceKm { get; private set; }
    public int Capacity { get; private set; }
    public int RegisteredCount { get; private set; }
    /// <summary>Giá gốc / fallback (API JSON: basePrice).</summary>
    public decimal Price { get; private set; }
    /// <summary>Platform VN-only — always VND.</summary>
    public string Currency { get; private set; } = "VND";
    public string Color { get; private set; } = "#FC4C02";
    /// <summary>Giờ xuất phát cự ly (ngày giải).</summary>
    public TimeOnly? StartAt { get; private set; }
    /// <summary>Giờ kết thúc / cutoff cự ly (ngày giải).</summary>
    public TimeOnly? EndAt { get; private set; }
    /// <summary>JSON arrays: osrmWaypoints, leafletPolyline, geoJson — FE map payload.</summary>
    public string RouteGeometryJson { get; private set; } = "{}";

    /// <summary>Tiền tố BIB theo cự ly (vd. 5K, HM). Null/empty = chỉ số 4 chữ số.</summary>
    public string? BibPrefix { get; private set; }
    /// <summary>Số bắt đầu dải BIB (1–9999, hiển thị/cấp dạng 0001…).</summary>
    public int? BibRangeStart { get; private set; }
    /// <summary>Số kết thúc dải BIB (1–9999, ≥ start).</summary>
    public int? BibRangeEnd { get; private set; }

    /// <summary>Organizer đã công bố BXH cự ly này cho runner/public.</summary>
    public bool ResultsPublished { get; private set; }

    public DateTime? ResultsPublishedAtUtc { get; private set; }

    public IReadOnlyCollection<EventCheckpoint> Checkpoints => _checkpoints.AsReadOnly();

    /// <summary>Same type and within 1 m: treated as the same checkpoint when the map id changed.</summary>
    private const double SameCheckpointKmTolerance = 0.001;

    /// <summary>
    /// Makes the checkpoints match the saved map course. Match by ExternalId first, then by same type within 1 m
    /// (the row keeps its Id and takes the new ExternalId). Unmatched rows are removed unless they have race data,
    /// in which case nothing changes and a DomainException names them.
    /// </summary>
    public CheckpointSyncOutcome SyncCheckpointsFromCourse(
        IReadOnlyList<CourseCheckpointDraft> drafts,
        IReadOnlySet<Guid> checkpointIdsWithData)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        ArgumentNullException.ThrowIfNull(checkpointIdsWithData);

        foreach (var draft in drafts)
        {
            if (string.IsNullOrWhiteSpace(draft.ExternalId) || draft.ExternalId.Trim().Length > 64)
            {
                throw new DomainException("Mã checkpoint trên bản đồ không hợp lệ (1–64 ký tự).");
            }

            if (draft.Cutoff is { } cutoff && cutoff.Trim().Length > 20)
            {
                throw new DomainException($"Cut-off của checkpoint \"{draft.Name.Trim()}\" quá dài (tối đa 20 ký tự).");
            }
        }

        var duplicate = drafts
            .GroupBy(draft => draft.ExternalId.Trim(), StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new DomainException($"Mã checkpoint \"{duplicate.Key}\" bị trùng trên bản đồ.");
        }

        var remaining = _checkpoints.ToList();
        var matches = new EventCheckpoint?[drafts.Count];
        for (var i = 0; i < drafts.Count; i++)
        {
            var externalId = drafts[i].ExternalId.Trim();
            var hit = remaining.FirstOrDefault(checkpoint => string.Equals(checkpoint.ExternalId, externalId, StringComparison.Ordinal));
            if (hit is not null)
            {
                matches[i] = hit;
                remaining.Remove(hit);
            }
        }

        for (var i = 0; i < drafts.Count; i++)
        {
            if (matches[i] is not null)
            {
                continue;
            }

            var draft = drafts[i];
            var hit = remaining
                .Where(checkpoint => checkpoint.Type == draft.Type
                                     && Math.Abs(checkpoint.Km - draft.Km) <= SameCheckpointKmTolerance)
                .OrderBy(checkpoint => Math.Abs(checkpoint.Km - draft.Km))
                .FirstOrDefault();
            if (hit is not null)
            {
                matches[i] = hit;
                remaining.Remove(hit);
            }
        }

        var blocked = remaining.Where(checkpoint => checkpointIdsWithData.Contains(checkpoint.Id)).ToList();
        if (blocked.Count > 0)
        {
            var names = string.Join(", ", blocked.OrderBy(checkpoint => checkpoint.SortOrder).Select(checkpoint => checkpoint.Name));
            throw new DomainException(
                $"Không thể xóa checkpoint đã có dữ liệu ghi nhận (lượt qua checkpoint hoặc timeline): {names}. "
                + "Hãy giữ checkpoint này trên bản đồ. Nếu dữ liệu chỉ đến từ giả lập, Reset lượt giả lập rồi lưu lại.");
        }

        foreach (var checkpoint in remaining)
        {
            _checkpoints.Remove(checkpoint);
        }

        var created = new List<EventCheckpoint>();
        var updated = 0;
        for (var i = 0; i < drafts.Count; i++)
        {
            var draft = drafts[i];
            if (matches[i] is { } existing)
            {
                existing.UpdateFromCourse(draft, i);
                updated++;
                continue;
            }

            var checkpoint = EventCheckpoint.Create(
                Id,
                draft.ExternalId,
                draft.Name,
                draft.Km,
                draft.Lat,
                draft.Lng,
                draft.Type,
                draft.Cutoff,
                null,
                i);
            _checkpoints.Add(checkpoint);
            created.Add(checkpoint);
        }

        return new CheckpointSyncOutcome(updated, remaining.Count, created);
    }

    public bool HasBibRange =>
        BibRangeStart is >= 1 and <= 9999
        && BibRangeEnd is >= 1 and <= 9999
        && BibRangeEnd.Value >= BibRangeStart.Value;

    public int BibRangeSize =>
        HasBibRange ? BibRangeEnd!.Value - BibRangeStart!.Value + 1 : 0;

    private EventDistance()
    {
    }

    public static EventDistance Create(
        Guid eventId,
        string name,
        double distanceKm,
        int capacity,
        decimal price,
        string? color = null,
        string? type = null,
        string? routeGeometryJson = null,
        IReadOnlyList<(string ExternalId, string Name, double Km, double Lat, double Lng, CheckpointType Type, string? Cutoff, int? Elevation)>? checkpoints = null,
        TimeOnly? startAt = null,
        TimeOnly? endAt = null)
    {
        if (distanceKm <= 0)
        {
            throw new DomainException("Quãng đường phải lớn hơn 0.");
        }

        if (capacity < 0)
        {
            throw new DomainException("Sức chứa cự ly không hợp lệ.");
        }

        var resolvedType = string.IsNullOrWhiteSpace(type) ? "OTHER" : type.Trim();

        var distance = new EventDistance
        {
            EventId = eventId,
            Type = resolvedType,
            Name = string.IsNullOrWhiteSpace(name) ? resolvedType : name.Trim(),
            DistanceKm = distanceKm,
            Capacity = capacity,
            RegisteredCount = 0,
            Price = price,
            Currency = "VND",
            Color = string.IsNullOrWhiteSpace(color) ? "#FC4C02" : color.Trim(),
            StartAt = startAt,
            EndAt = endAt,
            RouteGeometryJson = string.IsNullOrWhiteSpace(routeGeometryJson) ? "{}" : routeGeometryJson
        };

        if (checkpoints is not null)
        {
            for (var i = 0; i < checkpoints.Count; i++)
            {
                var c = checkpoints[i];
                distance._checkpoints.Add(EventCheckpoint.Create(
                    distance.Id,
                    c.ExternalId,
                    c.Name,
                    c.Km,
                    c.Lat,
                    c.Lng,
                    c.Type,
                    c.Cutoff,
                    c.Elevation,
                    i));
            }
        }

        return distance;
    }

    /// <summary>Cập nhật thuộc tính cự ly — giữ Id + RegisteredCount (map/staff/reg không gãy).</summary>
    public void UpdateDetails(
        string name,
        double distanceKm,
        int capacity,
        decimal price,
        TimeOnly? startAt,
        TimeOnly? endAt,
        string? type = null,
        string? color = null)
    {
        if (distanceKm <= 0)
        {
            throw new DomainException("Quãng đường phải lớn hơn 0.");
        }

        if (capacity < 0)
        {
            throw new DomainException("Sức chứa cự ly không hợp lệ.");
        }

        if (capacity < RegisteredCount)
        {
            throw new DomainException(
                $"Sức chứa ({capacity}) không được nhỏ hơn số VĐV đã đăng ký ({RegisteredCount}).");
        }

        if (HasBibRange && capacity != BibRangeSize)
        {
            throw new DomainException(
                $"Sức chứa ({capacity}) phải khớp dải BIB đã cấu hình ({BibRangeSize} số) cho cự ly «{Name}». " +
                "Hãy sửa dải BIB cho khớp trước.");
        }

        if (startAt.HasValue && endAt.HasValue && endAt.Value < startAt.Value)
        {
            throw new DomainException("Giờ kết thúc cự ly phải ≥ giờ xuất phát.");
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            Type = type.Trim();
        }

        Name = string.IsNullOrWhiteSpace(name) ? Type : name.Trim();
        DistanceKm = distanceKm;
        Capacity = capacity;
        Price = price;
        Currency = "VND";
        if (!string.IsNullOrWhiteSpace(color))
        {
            Color = color.Trim();
        }

        StartAt = startAt;
        EndAt = endAt;
    }

    /// <summary>Giữ dải BIB khi replace cự ly (PersistDistanceReplace → entity mới cùng Id).</summary>
    public void RestoreBibConfig(string? prefix, int? rangeStart, int? rangeEnd)
    {
        if (rangeStart is null || rangeEnd is null)
        {
            BibPrefix = null;
            BibRangeStart = null;
            BibRangeEnd = null;
            return;
        }

        SetBibConfig(prefix, rangeStart.Value, rangeEnd.Value);
    }

    /// <summary>Chuẩn hoá BIB gán tay / từ dải — phải nằm trong setup cự ly (không fallback 1001).</summary>
    public string NormalizeAssignedBib(string rawBib)
    {
        if (!HasBibRange)
        {
            throw new DomainException(
                $"Cự ly «{Name}» chưa cấu hình dải BIB — không thể cấp số.");
        }

        var raw = (rawBib ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            throw new DomainException("Thiếu số BIB.");
        }

        var prefix = (BibPrefix ?? string.Empty).Trim();
        int sequence;
        if (string.IsNullOrEmpty(prefix))
        {
            if (!int.TryParse(raw, out sequence))
            {
                throw new DomainException($"BIB «{raw}» không hợp lệ (cần số 0001–9999).");
            }
        }
        else if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(raw[prefix.Length..], out sequence))
            {
                throw new DomainException($"BIB «{raw}» không khớp prefix «{prefix}».");
            }
        }
        else if (int.TryParse(raw, out sequence))
        {
            // Cho phép nhập thuần số rồi gắn prefix cự ly.
        }
        else
        {
            throw new DomainException(
                $"BIB «{raw}» phải thuộc dải cự ly «{Name}» (prefix {prefix}, {BibRangeStart:D4}–{BibRangeEnd:D4}).");
        }

        if (sequence < BibRangeStart!.Value || sequence > BibRangeEnd!.Value)
        {
            throw new DomainException(
                $"BIB {sequence:D4} ngoài dải cự ly «{Name}» ({BibRangeStart:D4}–{BibRangeEnd:D4}).");
        }

        return FormatBib(prefix, sequence);
    }

    public void IncrementRegistered()
    {
        if (RegisteredCount >= Capacity && Capacity > 0)
        {
            throw new DomainException($"Cự ly {Type} đã hết slot.");
        }

        RegisteredCount++;
    }

    public void DecrementRegistered()
    {
        if (RegisteredCount > 0)
        {
            RegisteredCount--;
        }
    }

    public bool HasAvailableSlot => Capacity <= 0 || RegisteredCount < Capacity;

    /// <summary>
    /// Cấu hình dải BIB theo cự ly. Số luôn trong 1–9999 (cấp phát dạng 0001).
    /// Prefix theo type (số km: 15, HM→21, M→42).
    /// </summary>
    public void SetBibConfig(string? prefix, int rangeStart, int rangeEnd)
    {
        string? normalizedPrefix = null;
        var p = (prefix ?? string.Empty).Trim().ToUpperInvariant();
        if (p.Length > 0)
        {
            if (p.Length > 12)
            {
                throw new DomainException("Prefix BIB tối đa 12 ký tự.");
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(p, @"^[A-Z0-9]+-?$"))
            {
                throw new DomainException(
                    "Prefix BIB chỉ gồm chữ/số (A–Z, 0–9), có thể kết thúc bằng '-'.");
            }

            normalizedPrefix = p;
        }

        if (rangeStart < 1 || rangeEnd < 1)
        {
            throw new DomainException("Dải BIB không được là số 0 — dùng 0001–9999.");
        }

        if (rangeStart > 9999 || rangeEnd > 9999)
        {
            throw new DomainException("Số BIB tối đa 4 chữ số (0001–9999).");
        }

        if (rangeEnd < rangeStart)
        {
            throw new DomainException("Đến số phải ≥ từ số.");
        }

        var size = rangeEnd - rangeStart + 1;
        if (Capacity > 0 && size != Capacity)
        {
            throw new DomainException(
                size > Capacity
                    ? $"Dải BIB cự ly «{Name}» có {size} số — vượt sức chứa {Capacity}."
                    : $"Dải BIB cự ly «{Name}» chỉ có {size} số nhưng sức chứa là {Capacity}.");
        }

        BibPrefix = normalizedPrefix;
        BibRangeStart = rangeStart;
        BibRangeEnd = rangeEnd;
    }

    public void PublishResults(DateTime utcNow)
    {
        ResultsPublished = true;
        ResultsPublishedAtUtc = utcNow;
    }

    public void UnpublishResults()
    {
        ResultsPublished = false;
        ResultsPublishedAtUtc = null;
    }

    /// <summary>Ghép prefix + số 4 chữ số (1 → 0001).</summary>
    public static string FormatBib(string? prefix, int sequence)
    {
        if (sequence < 1 || sequence > 9999)
        {
            throw new DomainException("Số BIB phải trong 0001–9999.");
        }

        var num = sequence.ToString("D4");
        var p = (prefix ?? string.Empty).Trim();
        return string.IsNullOrEmpty(p) ? num : $"{p}{num}";
    }
}
