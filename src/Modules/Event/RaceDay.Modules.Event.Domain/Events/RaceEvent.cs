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

    public void ReplaceDistances(IEnumerable<EventDistance> distances)
    {
        EnsureEditable();
        _distances.Clear();
        foreach (var d in distances)
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
        if (Status is not (RaceEventStatus.Draft or RaceEventStatus.Rejected))
        {
            throw new DomainException("Chỉ hồ sơ nháp hoặc bị từ chối mới được gửi duyệt.");
        }

        if (_distances.Count == 0)
        {
            throw new DomainException("Cần ít nhất một cự ly trước khi gửi duyệt.");
        }

        var missingRequired = _documents.Where(d => d.Required && string.IsNullOrWhiteSpace(d.FileName) && string.IsNullOrWhiteSpace(d.FileUrl)).ToList();
        if (missingRequired.Count > 0)
        {
            throw new DomainException($"Thiếu hồ sơ bắt buộc: {string.Join(", ", missingRequired.Select(x => x.DocumentKey))}.");
        }

        Status = RaceEventStatus.PendingApproval;
        RejectionReason = null;
        SubmittedAtUtc = DateTime.UtcNow;
        Touch();
        Raise(new RaceEventSubmittedDomainEvent(Id, Name, OrganizerUserId));
    }

    public void Approve()
    {
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
        if (Status == RaceEventStatus.RegistrationOpen)
        {
            throw new DomainException("Giải đã mở đăng ký rồi.");
        }

        if (Status is not (RaceEventStatus.Approved or RaceEventStatus.Closed))
        {
            throw new DomainException("Chỉ giải đã duyệt (Approved) hoặc đã đóng ĐK (Closed) mới mở đăng ký. Duyệt admin ≠ tự mở ĐK.");
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

    public EventDistance GetDistance(Guid distanceId)
        => _distances.FirstOrDefault(d => d.Id == distanceId)
           ?? throw new DomainException("Cự ly không thuộc giải này.");

    public void EnsureAcceptsRegistration()
    {
        if (Status != RaceEventStatus.RegistrationOpen)
        {
            throw new DomainException("Giải chưa mở đăng ký.");
        }
    }

    private void EnsureEditable()
    {
        if (Status is not (RaceEventStatus.Draft or RaceEventStatus.Rejected))
        {
            throw new DomainException("Không thể chỉnh sửa giải ở trạng thái hiện tại.");
        }
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
