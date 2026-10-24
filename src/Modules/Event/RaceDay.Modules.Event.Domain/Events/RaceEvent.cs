using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Domain.Events;

public sealed class RaceEvent : AggregateRoot
{
    private readonly List<EventDistance> _distances = [];
    private readonly List<EventDocument> _documents = [];

    public Guid OrganizerUserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Tagline { get; private set; }
    public string? BannerImage { get; private set; }
    public string Location { get; private set; } = string.Empty;
    public GeoPoint Coordinates { get; private set; } = new(0, 0);
    public DateOnly EventDate { get; private set; }
    /// <summary>Ngày kết thúc giải (nullable).</summary>
    public DateOnly? EndDate { get; private set; }
    public TimeOnly? StartTime { get; private set; }
    public RaceEventStatus Status { get; private set; } = RaceEventStatus.Draft;
    public OrganizerInfo Organizer { get; private set; } = null!;
    public int TotalRunnersCap { get; private set; }
    public WeatherInfo? Weather { get; private set; }
    public PermitDocument? Permit { get; private set; }
    /// <summary>EventDoc ticketPricing JSON.</summary>
    public string TicketPricingJson { get; private set; } = "{}";
    /// <summary>EventDoc merchandise JSON.</summary>
    public string MerchandiseJson { get; private set; } = "{}";
    /// <summary>EventDoc rules JSON.</summary>
    public string RulesJson { get; private set; } = "{}";
    /// <summary>EventDoc rewards JSON.</summary>
    public string RewardsJson { get; private set; } = "{}";
    public string? Description { get; private set; }
    public string? RejectionReason { get; private set; }
    public Guid? PackagePurchaseId { get; private set; }
    public DateTime? PackageExpiresAtUtc { get; private set; }
    /// <summary>Lịch mở ĐK (UTC). Job auto-open khi Approved và now &gt;= OpensAt.</summary>
    public DateTime? RegistrationOpensAtUtc { get; private set; }
    /// <summary>Lịch đóng ĐK (UTC). Không được sau PackageExpiresAtUtc.</summary>
    public DateTime? RegistrationClosesAtUtc { get; private set; }
    /// <summary>Legacy — không còn dùng để cấp BIB. SoT = dải trên từng EventDistance.</summary>
    public string? BibPrefix { get; private set; }
    /// <summary>Legacy — không còn dùng để cấp BIB.</summary>
    public int? BibRangeStart { get; private set; }
    /// <summary>Legacy — không còn dùng để cấp BIB.</summary>
    public int? BibRangeEnd { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? SubmittedAtUtc { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }

    public IReadOnlyCollection<EventDistance> Distances => _distances.AsReadOnly();
    public IReadOnlyCollection<EventDocument> Documents => _documents.AsReadOnly();
    public int ActiveRunners => _distances.Sum(d => d.RegisteredCount);

    private RaceEvent()
    {
    }

    public static RaceEvent CreateDraft(
        Guid organizerUserId,
        string name,
        string location,
        DateOnly eventDate,
        GeoPoint coordinates,
        OrganizerInfo organizer,
        int totalRunnersCap,
        string? tagline = null,
        TimeOnly? startTime = null,
        string? description = null,
        WeatherInfo? weather = null,
        PermitDocument? permit = null,
        string? ticketPricingJson = null,
        string? merchandiseJson = null,
        string? rulesJson = null,
        string? rewardsJson = null,
        string? bannerImage = null)
    {
        if (organizerUserId == Guid.Empty)
        {
            throw new DomainException("OrganizerUserId không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Tên giải không được để trống.");
        }

        if (string.IsNullOrWhiteSpace(location))
        {
            throw new DomainException("Địa điểm không được để trống.");
        }

        if (totalRunnersCap < 0)
        {
            throw new DomainException("Tổng suất VĐV không hợp lệ.");
        }

        var now = DateTime.UtcNow;
        var race = new RaceEvent
        {
            OrganizerUserId = organizerUserId,
            Name = name.Trim(),
            Tagline = tagline?.Trim(),
            BannerImage = string.IsNullOrWhiteSpace(bannerImage) ? null : bannerImage.Trim(),
            Location = location.Trim(),
            Coordinates = coordinates,
            EventDate = eventDate,
            StartTime = startTime,
            Status = RaceEventStatus.Draft,
            Organizer = organizer,
            TotalRunnersCap = totalRunnersCap,
            Weather = weather,
            Permit = permit,
            TicketPricingJson = string.IsNullOrWhiteSpace(ticketPricingJson) ? "{}" : ticketPricingJson,
            MerchandiseJson = string.IsNullOrWhiteSpace(merchandiseJson) ? "{}" : merchandiseJson,
            RulesJson = string.IsNullOrWhiteSpace(rulesJson) ? "{}" : rulesJson,
            RewardsJson = string.IsNullOrWhiteSpace(rewardsJson) ? "{}" : rewardsJson,
            Description = description?.Trim(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        race.Raise(new RaceEventCreatedDomainEvent(race.Id, race.Name, race.OrganizerUserId));
        return race;
    }

    public void SetBannerImage(string? bannerImage)
    {
        BannerImage = string.IsNullOrWhiteSpace(bannerImage) ? null : bannerImage.Trim();
        Touch();
    }

    public void SetPackageInfo(Guid purchaseId, DateTime expiresAtUtc)
    {
        PackagePurchaseId = purchaseId;
        PackageExpiresAtUtc = expiresAtUtc;
        Touch();
    }

    /// <summary>Validate quyền + sức chứa gói — không đụng collection (dùng khi persist ngoài EF navigation).</summary>
    public void AssertCanReplaceDistances(IReadOnlyList<EventDistance> distances)
    {
        EnsureEditable();
        if (distances.Count == 0)
        {
            throw new DomainException("Cần ít nhất một cự ly.");
        }

        if (PackagePurchaseId.HasValue && TotalRunnersCap > 0)
        {
            var sum = distances.Sum(d => d.Capacity);
            if (sum > TotalRunnersCap)
            {
                throw new DomainException(
                    $"Tổng slot cự ly ({sum}) vượt quy mô gói ({TotalRunnersCap} VĐV).");
            }
        }
    }

    public void ReplaceDistances(IEnumerable<EventDistance> distances)
    {
        var list = distances.ToList();
        AssertCanReplaceDistances(list);

        _distances.Clear();
        foreach (var d in list)
        {
            _distances.Add(d);
        }

        Touch();
    }

    public void SetTicketPricingJson(string? ticketPricingJson)
    {
        EnsureEditable();
        TicketPricingJson = string.IsNullOrWhiteSpace(ticketPricingJson) ? "{}" : ticketPricingJson;
        Touch();
    }

    public void ReplaceDocuments(IEnumerable<EventDocument> documents)
    {
        EnsureEditable();
        _documents.Clear();
        foreach (var doc in documents)
        {
            _documents.Add(doc);
        }

        Touch();
    }

    public void SubmitForApproval()
    {
        EnsurePackageNotExpired(DateTime.UtcNow);
        if (Status is not (RaceEventStatus.Draft or RaceEventStatus.Rejected))
        {
            throw new DomainException("Chỉ hồ sơ nháp hoặc bị từ chối mới được gửi duyệt.");
        }

        if (_distances.Count == 0)
        {
            throw new DomainException("Cần ít nhất một cự ly trước khi gửi duyệt.");
        }

        EnsureDistanceBibConfigsReady();

        // Bắt buộc kể cả khi danh sách documents còn trống (chưa Save docs).
        string[] requiredDocKeys = ["official-letter", "security"];
        var missingKeys = requiredDocKeys
            .Where(key =>
            {
                var doc = _documents.FirstOrDefault(d =>
                    string.Equals(d.DocumentKey, key, StringComparison.OrdinalIgnoreCase));
                return doc is null
                       || (string.IsNullOrWhiteSpace(doc.FileName) && string.IsNullOrWhiteSpace(doc.FileUrl));
            })
            .ToList();
        if (missingKeys.Count > 0)
        {
            throw new DomainException($"Thiếu hồ sơ bắt buộc: {string.Join(", ", missingKeys)}.");
        }

        var missingRequired = _documents
            .Where(d => d.Required && string.IsNullOrWhiteSpace(d.FileName) && string.IsNullOrWhiteSpace(d.FileUrl))
            .ToList();
        if (missingRequired.Count > 0)
        {
            throw new DomainException($"Thiếu hồ sơ bắt buộc: {string.Join(", ", missingRequired.Select(x => x.DocumentKey))}.");
        }

        if (string.IsNullOrWhiteSpace(Location)
            || Location.Contains("Chưa cập nhật", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("Cần cập nhật địa điểm thật trước khi gửi duyệt.");
        }

        Status = RaceEventStatus.PendingApproval;
        RejectionReason = null;
        SubmittedAtUtc = DateTime.UtcNow;
        Touch();
        Raise(new RaceEventSubmittedDomainEvent(Id, Name, OrganizerUserId));
    }

    public void Approve()
    {
        EnsurePackageNotExpired(DateTime.UtcNow);
        if (Status != RaceEventStatus.PendingApproval)
        {
            throw new DomainException("Chỉ giải đang chờ duyệt mới được phê duyệt.");
        }

        Status = RaceEventStatus.Approved;
        RejectionReason = null;
        ReviewedAtUtc = DateTime.UtcNow;
        Organizer.MarkVerified();
        Touch();
        Raise(new RaceEventApprovedDomainEvent(Id, Name, OrganizerUserId));
    }

    public void Reject(string reason)
    {
        if (Status != RaceEventStatus.PendingApproval)
        {
            throw new DomainException("Chỉ giải đang chờ duyệt mới bị từ chối.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Lý do từ chối không được để trống.");
        }

        Status = RaceEventStatus.Rejected;
        RejectionReason = reason.Trim();
        ReviewedAtUtc = DateTime.UtcNow;
        Touch();
        Raise(new RaceEventRejectedDomainEvent(Id, Name, OrganizerUserId, RejectionReason));
    }

    public void OpenRegistration()
    {
        EnsurePackageNotExpired(DateTime.UtcNow);
        if (Status == RaceEventStatus.RegistrationOpen)
        {
            throw new DomainException("Giải đã mở đăng ký rồi.");
        }

        if (Status is not (RaceEventStatus.Approved or RaceEventStatus.Closed))
        {
            throw new DomainException("Chỉ giải đã duyệt (Approved) hoặc đã đóng ĐK (Closed) mới mở đăng ký. Duyệt admin ≠ tự mở ĐK.");
        }

        if (RegistrationClosesAtUtc.HasValue && DateTime.UtcNow >= RegistrationClosesAtUtc.Value)
        {
            throw new DomainException("Đã qua thời điểm đóng đăng ký.");
        }

        if (_distances.Count == 0)
        {
            throw new DomainException("Cần ít nhất một cự ly trước khi mở đăng ký.");
        }

        EnsureDistanceBibConfigsReady();

        if (string.IsNullOrWhiteSpace(Location)
            || Location.Contains("Chưa cập nhật", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("Cần cập nhật địa điểm thật trước khi mở đăng ký.");
        }

        if (!_documents.Any(d =>
                string.Equals(d.DocumentKey, "official-letter", StringComparison.OrdinalIgnoreCase)
                && (!string.IsNullOrWhiteSpace(d.FileUrl) || !string.IsNullOrWhiteSpace(d.FileName)))
            || !_documents.Any(d =>
                string.Equals(d.DocumentKey, "security", StringComparison.OrdinalIgnoreCase)
                && (!string.IsNullOrWhiteSpace(d.FileUrl) || !string.IsNullOrWhiteSpace(d.FileName))))
        {
            throw new DomainException("Thiếu hồ sơ bắt buộc (công văn / phương án an ninh) trước khi mở đăng ký.");
        }

        Status = RaceEventStatus.RegistrationOpen;
        Touch();
    }

    public void CloseRegistration()
    {
        if (Status != RaceEventStatus.RegistrationOpen)
        {
            throw new DomainException("Giải hiện không ở trạng thái mở đăng ký.");
        }

        Status = RaceEventStatus.Closed;
        Touch();
    }

    /// <summary>Job: Approved + đã tới OpensAt → mở ĐK (idempotent).</summary>
    public bool TryAutoOpenRegistration(DateTime utcNow)
    {
        if (Status != RaceEventStatus.Approved)
            return false;
        if (!RegistrationOpensAtUtc.HasValue || utcNow < RegistrationOpensAtUtc.Value)
            return false;
        if (IsPackageExpired(utcNow))
            return false;
        if (RegistrationClosesAtUtc.HasValue && utcNow >= RegistrationClosesAtUtc.Value)
            return false;
        if (_distances.Count == 0 || _distances.Any(d => !d.HasBibRange))
            return false;

        Status = RaceEventStatus.RegistrationOpen;
        Touch();
        return true;
    }

    /// <summary>Job: RegistrationOpen + đã tới ClosesAt → đóng ĐK (idempotent).</summary>
    public bool TryAutoCloseRegistration(DateTime utcNow)
    {
        if (Status != RaceEventStatus.RegistrationOpen)
            return false;
        if (!RegistrationClosesAtUtc.HasValue || utcNow < RegistrationClosesAtUtc.Value)
            return false;

        Status = RaceEventStatus.Closed;
        Touch();
        return true;
    }

    public void SetRegistrationWindow(DateTime? opensAtUtc, DateTime? closesAtUtc)
    {
        EnsurePackageNotExpired(DateTime.UtcNow);
        if (Status is RaceEventStatus.Suspended or RaceEventStatus.Cancelled or RaceEventStatus.Completed)
        {
            throw new DomainException("Không thể đặt lịch ĐK ở trạng thái hiện tại.");
        }

        if (opensAtUtc.HasValue && closesAtUtc.HasValue && closesAtUtc.Value < opensAtUtc.Value)
        {
            throw new DomainException("Thời điểm đóng ĐK phải sau thời điểm mở ĐK.");
        }

        if (closesAtUtc.HasValue && PackageExpiresAtUtc.HasValue && closesAtUtc.Value > PackageExpiresAtUtc.Value)
        {
            throw new DomainException("Đóng đăng ký không được sau hạn gói tổ chức.");
        }

        RegistrationOpensAtUtc = opensAtUtc;
        RegistrationClosesAtUtc = closesAtUtc;
        Touch();
    }

    public EventDistance GetDistance(Guid distanceId)
        => _distances.FirstOrDefault(d => d.Id == distanceId)
           ?? throw new DomainException("Cự ly không thuộc giải này.");

    public void EnsureAcceptsRegistration()
    {
        EnsurePackageNotExpired(DateTime.UtcNow);
        if (Status == RaceEventStatus.Suspended)
        {
            throw new DomainException("Giải đã bị khóa vì hết hạn gói tổ chức.");
        }
        if (Status != RaceEventStatus.RegistrationOpen)
        {
            throw new DomainException("Giải chưa mở đăng ký.");
        }
    }

    public bool IsPackageExpired(DateTime utcNow)
        => PackageExpiresAtUtc.HasValue && utcNow >= PackageExpiresAtUtc.Value;

    public void EnsurePackageNotExpired(DateTime utcNow)
    {
        if (IsPackageExpired(utcNow) || Status == RaceEventStatus.Suspended)
        {
            throw new DomainException("Gói tổ chức đã hết hạn. Giải đã bị khóa.");
        }
    }

    /// <summary>Khóa giải khi hết hạn gói (idempotent).</summary>
    public bool TrySuspendForExpiredPackage(DateTime utcNow)
    {
        if (!IsPackageExpired(utcNow))
            return false;
        if (Status is RaceEventStatus.Suspended or RaceEventStatus.Cancelled or RaceEventStatus.Completed)
            return false;

        Status = RaceEventStatus.Suspended;
        Touch();
        return true;
    }

    private void EnsureEditable()
    {
        EnsurePackageNotExpired(DateTime.UtcNow);
        if (Status is not (RaceEventStatus.Draft or RaceEventStatus.Rejected))
        {
            throw new DomainException("Không thể chỉnh sửa giải ở trạng thái hiện tại.");
        }
    }

    public void UpdateDraftProfile(
        string? name,
        string? tagline,
        string? location,
        DateOnly? eventDate,
        TimeOnly? startTime,
        string? description,
        string? bannerImage,
        int? totalRunnersCap,
        OrganizerInfo? organizer,
        string? rulesJson,
        string? ticketPricingJson = null,
        string? rewardsJson = null,
        DateOnly? endDate = null,
        bool clearEndDate = false)
    {
        EnsureEditable();

        if (name is not null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new DomainException("Tên giải không được để trống.");
            }

            Name = name.Trim();
        }

        if (tagline is not null) Tagline = tagline.Trim();
        if (location is not null)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                throw new DomainException("Địa điểm không được để trống.");
            }

            Location = location.Trim();
        }
        if (eventDate.HasValue) EventDate = eventDate.Value;
        if (clearEndDate) EndDate = null;
        else if (endDate.HasValue) EndDate = endDate.Value;
        if (startTime.HasValue) StartTime = startTime.Value;
        if (description is not null) Description = description.Trim();
        if (bannerImage is not null) BannerImage = string.IsNullOrWhiteSpace(bannerImage) ? null : bannerImage.Trim();
        if (totalRunnersCap.HasValue)
        {
            // Quy mô VĐV cả giải = theo gói đã mua — không cho organizer sửa.
            if (PackagePurchaseId.HasValue)
            {
                if (totalRunnersCap.Value != TotalRunnersCap)
                {
                    throw new DomainException(
                        $"Quy mô VĐV ({TotalRunnersCap}) theo gói đăng ký — không thể thay đổi.");
                }
            }
            else
            {
                if (totalRunnersCap.Value < 0)
                {
                    throw new DomainException("Tổng suất VĐV không hợp lệ.");
                }

                TotalRunnersCap = totalRunnersCap.Value;
            }
        }
        if (organizer is not null) Organizer = organizer;
        if (rulesJson is not null) RulesJson = string.IsNullOrWhiteSpace(rulesJson) ? "{}" : rulesJson;
        if (ticketPricingJson is not null)
            TicketPricingJson = string.IsNullOrWhiteSpace(ticketPricingJson) ? "{}" : ticketPricingJson;
        if (rewardsJson is not null)
            RewardsJson = string.IsNullOrWhiteSpace(rewardsJson) ? "{}" : rewardsJson;

        Touch();
    }

    /// <summary>
    /// Legacy API cấp giải — đã ngưng. Dùng PUT bib-configs theo từng cự ly.
    /// </summary>
    public void SetBibConfig(string? prefix, int? rangeStart, int? rangeEnd)
    {
        _ = prefix;
        _ = rangeStart;
        _ = rangeEnd;
        throw new DomainException(
            "BIB không còn cấu hình theo cả giải. Hãy setup dải BIB theo từng cự ly (trang Cấu hình BIB).");
    }

    /// <summary>
    /// Ghi dải BIB theo từng cự ly — validate toàn bộ rồi mới apply (fail-safe).
    /// </summary>
    public void ApplyDistanceBibConfigs(
        IReadOnlyList<(Guid DistanceId, string? Prefix, int RangeStart, int RangeEnd)> configs)
    {
        EnsureCanEditBibConfig();

        if (_distances.Count == 0)
        {
            throw new DomainException("Chưa có cự ly — hãy thêm cự ly trước khi cấu hình BIB.");
        }

        if (configs.Count == 0)
        {
            throw new DomainException("Cần cấu hình BIB cho ít nhất một cự ly.");
        }

        var byId = _distances.ToDictionary(d => d.Id);
        var seen = new HashSet<Guid>();
        var prepared = new List<(EventDistance Distance, string? Prefix, int Start, int End, int Size, string Name)>();

        foreach (var cfg in configs)
        {
            if (!byId.TryGetValue(cfg.DistanceId, out var distance))
            {
                throw new DomainException("Cự ly không thuộc giải này.");
            }

            if (!seen.Add(cfg.DistanceId))
            {
                throw new DomainException($"Trùng cấu hình BIB cho cự ly «{distance.Name}».");
            }

            // Validate without mutating — mirror SetBibConfig rules against current capacity.
            ValidateBibRangeForDistance(distance, cfg.Prefix, cfg.RangeStart, cfg.RangeEnd, out var normalizedPrefix);
            var size = cfg.RangeEnd - cfg.RangeStart + 1;
            prepared.Add((distance, normalizedPrefix, cfg.RangeStart, cfg.RangeEnd, size, distance.Name));
        }

        if (seen.Count != _distances.Count)
        {
            throw new DomainException(
                "Cần cấu hình BIB cho đủ mọi cự ly của giải rồi mới lưu.");
        }

        for (var i = 0; i < prepared.Count; i++)
        {
            for (var j = i + 1; j < prepared.Count; j++)
            {
                var a = prepared[i];
                var b = prepared[j];
                if (a.Start <= b.End && b.Start <= a.End)
                {
                    throw new DomainException(
                        $"Dải BIB trùng giữa «{a.Name}» ({a.Start:D4}–{a.End:D4}) và «{b.Name}» ({b.Start:D4}–{b.End:D4}).");
                }
            }
        }

        var totalBibSlots = prepared.Sum(x => x.Size);
        if (TotalRunnersCap > 0 && totalBibSlots > TotalRunnersCap)
        {
            throw new DomainException(
                $"Tổng dải BIB ({totalBibSlots}) vượt quy mô giải/gói ({TotalRunnersCap} VĐV).");
        }

        foreach (var p in prepared)
        {
            p.Distance.SetBibConfig(p.Prefix, p.Start, p.End);
        }

        ClearEventLevelBib();
        Touch();
    }

    public void EnsureDistanceBibConfigsReady()
    {
        if (_distances.Count == 0)
        {
            throw new DomainException("Chưa có cự ly.");
        }

        var missing = _distances.Where(d => !d.HasBibRange).Select(d => d.Name).ToList();
        if (missing.Count > 0)
        {
            throw new DomainException(
                $"Chưa cấu hình dải BIB cho cự ly: {string.Join(", ", missing)}. " +
                "Vào Cấu hình BIB sau khi setup cự ly — hệ thống không tự cấp 1001…");
        }
    }

    private static void ValidateBibRangeForDistance(
        EventDistance distance,
        string? prefix,
        int rangeStart,
        int rangeEnd,
        out string? normalizedPrefix)
    {
        normalizedPrefix = null;
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
        if (distance.Capacity > 0 && size != distance.Capacity)
        {
            throw new DomainException(
                size > distance.Capacity
                    ? $"Dải BIB cự ly «{distance.Name}» có {size} số — vượt sức chứa {distance.Capacity}."
                    : $"Dải BIB cự ly «{distance.Name}» chỉ có {size} số nhưng sức chứa là {distance.Capacity}.");
        }
    }

    private void ClearEventLevelBib()
    {
        BibPrefix = null;
        BibRangeStart = null;
        BibRangeEnd = null;
    }

    private void EnsureCanEditBibConfig()
    {
        EnsurePackageNotExpired(DateTime.UtcNow);
        if (Status is RaceEventStatus.Completed
            or RaceEventStatus.Cancelled
            or RaceEventStatus.Suspended)
        {
            throw new DomainException("Không thể đổi cấu hình BIB ở trạng thái hiện tại.");
        }
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
