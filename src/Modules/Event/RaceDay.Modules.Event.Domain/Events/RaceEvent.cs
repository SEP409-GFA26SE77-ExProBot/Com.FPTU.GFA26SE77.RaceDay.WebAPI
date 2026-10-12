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

    public void ReplaceDistances(IEnumerable<EventDistance> distances)
    {
        EnsureEditable();
        var list = distances.ToList();
        if (PackagePurchaseId.HasValue && TotalRunnersCap > 0)
        {
            var sum = list.Sum(d => d.Capacity);
            if (sum > TotalRunnersCap)
            {
                throw new DomainException(
                    $"Tổng slot cự ly ({sum}) vượt quy mô gói ({TotalRunnersCap} VĐV).");
            }
        }

        _distances.Clear();
        foreach (var d in list)
        {
            _distances.Add(d);
        }

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

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
