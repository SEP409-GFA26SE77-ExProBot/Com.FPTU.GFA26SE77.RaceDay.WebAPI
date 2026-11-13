using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;
using RaceDay.Modules.Event.Domain.Events;

namespace RaceDay.Modules.Event.Domain.Registrations;

public sealed class EventRegistration : AggregateRoot
{
    public Guid EventId { get; private set; }
    public Guid DistanceId { get; private set; }
    public Guid UserId { get; private set; }
    public RegistrationStatus Status { get; private set; } = RegistrationStatus.PendingPayment;
    public RaceStatus RaceStatus { get; private set; } = RaceStatus.NotStarted;
    public string FullName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public DateOnly? DateOfBirth { get; private set; }
    public string? Gender { get; private set; }
    public string? IdNumber { get; private set; }
    public int? HeightCm { get; private set; }
    public int? WeightKg { get; private set; }
    public string? BloodType { get; private set; }
    public string? EmergencyName { get; private set; }
    public string? EmergencyPhone { get; private set; }
    public string? ShirtType { get; private set; }
    public string? ShirtSize { get; private set; }
    public string? ShirtColor { get; private set; }
    public string MedicalCodesJson { get; private set; } = "[]";
    public string? HealthCertFileName { get; private set; }
    public string? HealthCertUrl { get; private set; }
    public bool LiabilityAccepted { get; private set; }
    public DateTime? LiabilityAcceptedAtUtc { get; private set; }
    public string? RejectionReason { get; private set; }
    public string? BibNumber { get; private set; }
    /// <summary>Giá chốt lúc tạo ĐK (theo phase lúc đó) — checkout/refund bám snapshot này.</summary>
    public decimal? QuotedAmount { get; private set; }
    public string? QuotedPhaseKey { get; private set; }
    public string? QuotedPhaseName { get; private set; }
    /// <summary>UTC khi staff/organizer quét QR check-in cổng.</summary>
    public DateTime? CheckedInAtUtc { get; private set; }
    public Guid? CheckedInByUserId { get; private set; }
    public DateTime? FinishedAtUtc { get; private set; }
    public long? ResultSeconds { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }


    public DateTime? StartedAtUtc { get; private set; }

   
    private EventRegistration()
    {
    }

    public static EventRegistration Create(
        Guid eventId,
        Guid distanceId,
        Guid userId,
        string fullName,
        string email,
        string phone,
        DateOnly? dateOfBirth,
        string? gender,
        string? idNumber,
        int? heightCm,
        int? weightKg,
        string? bloodType,
        string? emergencyName,
        string? emergencyPhone,
        string? shirtType,
        string? shirtSize,
        string? shirtColor,
        string medicalCodesJson,
        string? healthCertFileName,
        string? healthCertUrl,
        bool liabilityAccepted)
    {
        if (eventId == Guid.Empty || distanceId == Guid.Empty || userId == Guid.Empty)
        {
            throw new DomainException("Tham chiếu đăng ký không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new DomainException("Họ tên không được để trống.");
        }

        if (!liabilityAccepted)
        {
            throw new DomainException("Cần chấp nhận điều khoản trách nhiệm.");
        }

        var now = DateTime.UtcNow;
        var reg = new EventRegistration
        {
            EventId = eventId,
            DistanceId = distanceId,
            UserId = userId,
            Status = RegistrationStatus.PendingPayment,
            FullName = fullName.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            Phone = phone.Trim(),
            DateOfBirth = dateOfBirth,
            Gender = gender?.Trim(),
            IdNumber = idNumber?.Trim(),
            HeightCm = heightCm,
            WeightKg = weightKg,
            BloodType = bloodType?.Trim(),
            EmergencyName = emergencyName?.Trim(),
            EmergencyPhone = emergencyPhone?.Trim(),
            ShirtType = shirtType?.Trim(),
            ShirtSize = shirtSize?.Trim(),
            ShirtColor = shirtColor?.Trim(),
            MedicalCodesJson = string.IsNullOrWhiteSpace(medicalCodesJson) ? "[]" : medicalCodesJson,
            HealthCertFileName = healthCertFileName?.Trim(),
            HealthCertUrl = healthCertUrl?.Trim(),
            LiabilityAccepted = true,
            LiabilityAcceptedAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        reg.Raise(new RegistrationCreatedDomainEvent(reg.Id, reg.EventId, reg.UserId));
        return reg;
    }

    public void LockQuotedPrice(decimal amount, string? phaseKey, string? phaseName)
    {
        if (amount < 0)
        {
            throw new DomainException("Giá vé không hợp lệ.");
        }

        QuotedAmount = amount;
        QuotedPhaseKey = string.IsNullOrWhiteSpace(phaseKey) ? null : phaseKey.Trim();
        QuotedPhaseName = string.IsNullOrWhiteSpace(phaseName) ? null : phaseName.Trim();
        Touch();
    }

    public void MarkPaidPendingApproval()
    {
        if (Status != RegistrationStatus.PendingPayment)
        {
            throw new DomainException("Đăng ký không ở trạng thái chờ thanh toán.");
        }

        Status = RegistrationStatus.PendingApproval;
        Touch();
    }

    /// <summary>
    /// Có bệnh lý đặc biệt cần BTC duyệt tay.
    /// <c>[]</c>, rỗng, hoặc chỉ <c>none</c> → không bệnh lý → đủ điều kiện auto-approve.
    /// </summary>
    public bool HasSpecialMedicalConditions()
    {
        var raw = (MedicalCodesJson ?? string.Empty).Trim();
        if (raw.Length == 0 || raw == "[]" || raw == "null")
        {
            return false;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return !string.Equals(raw, "none", StringComparison.OrdinalIgnoreCase);
            }

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var code = el.ValueKind == System.Text.Json.JsonValueKind.String
                    ? el.GetString()?.Trim()
                    : el.ToString().Trim();
                if (string.IsNullOrEmpty(code))
                {
                    continue;
                }

                if (!code.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch (System.Text.Json.JsonException)
        {
            return !raw.Equals("none", StringComparison.OrdinalIgnoreCase);
        }
    }

    public void Confirm(string? bibNumber = null)
    {
        if (Status is not (RegistrationStatus.PendingApproval or RegistrationStatus.PendingPayment))
        {
            throw new DomainException("Không thể xác nhận đăng ký ở trạng thái hiện tại.");
        }

        Status = RegistrationStatus.Confirmed;
        BibNumber = bibNumber?.Trim();
        RejectionReason = null;
        Touch();
        Raise(new RegistrationConfirmedDomainEvent(Id, EventId, UserId));
    }

    public void Reject(string reason)
    {
        if (Status != RegistrationStatus.PendingApproval)
        {
            throw new DomainException("Chỉ đăng ký chờ duyệt mới bị từ chối.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Lý do từ chối không được để trống.");
        }

        Status = RegistrationStatus.Rejected;
        RejectionReason = reason.Trim();
        Touch();
    }

    /// <summary>
    /// Legacy: chỉ còn cho đơn Rejected cũ chưa bị auto-cancel sau refund.
    /// Policy hiện tại: reject → hủy slot + hoàn theo ngày → Cancelled (không nộp lại trên đơn cũ).
    /// </summary>
    public void UpdateProfileAndResubmit(
        string fullName,
        string email,
        string phone,
        DateOnly? dateOfBirth,
        string? gender,
        string? idNumber,
        int? heightCm,
        int? weightKg,
        string? bloodType,
        string? emergencyName,
        string? emergencyPhone,
        string? shirtType,
        string? shirtSize,
        string? shirtColor,
        string medicalCodesJson,
        string? healthCertFileName,
        string? healthCertUrl,
        bool liabilityAccepted)
    {
        if (Status != RegistrationStatus.Rejected)
        {
            throw new DomainException("Chỉ đơn bị từ chối mới được sửa và nộp lại.");
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new DomainException("Họ tên không được để trống.");
        }

        if (!liabilityAccepted)
        {
            throw new DomainException("Cần chấp nhận điều khoản trách nhiệm.");
        }

        FullName = fullName.Trim();
        Email = email.Trim().ToLowerInvariant();
        Phone = phone.Trim();
        DateOfBirth = dateOfBirth;
        Gender = gender?.Trim();
        IdNumber = idNumber?.Trim();
        HeightCm = heightCm;
        WeightKg = weightKg;
        BloodType = bloodType?.Trim();
        EmergencyName = emergencyName?.Trim();
        EmergencyPhone = emergencyPhone?.Trim();
        ShirtType = shirtType?.Trim();
        ShirtSize = shirtSize?.Trim();
        ShirtColor = shirtColor?.Trim();
        MedicalCodesJson = string.IsNullOrWhiteSpace(medicalCodesJson) ? "[]" : medicalCodesJson;
        HealthCertFileName = healthCertFileName?.Trim();
        HealthCertUrl = healthCertUrl?.Trim();
        LiabilityAccepted = true;
        LiabilityAcceptedAtUtc = DateTime.UtcNow;
        RejectionReason = null;
        Status = RegistrationStatus.PendingApproval;
        Touch();
    }

    public void CancelUnpaid()
    {
        if (Status != RegistrationStatus.PendingPayment)
        {
            throw new DomainException("Chỉ hủy được đơn đang chờ thanh toán.");
        }

        Status = RegistrationStatus.Cancelled;
        Touch();
    }

    /// <summary>
    /// After paid refund — hủy ĐK, xóa BIB (nhả số cho VĐV khác), slot đã ReleaseSlotAsync riêng.
    /// </summary>
    public void MarkCancelledAfterRefund()
    {
        if (Status is RegistrationStatus.Cancelled)
        {
            BibNumber = null;
            Touch();
            return;
        }

        if (Status is not (RegistrationStatus.PendingApproval or RegistrationStatus.Confirmed or RegistrationStatus.Rejected))
        {
            throw new DomainException("Không thể hủy đăng ký đã hoàn ở trạng thái hiện tại.");
        }

        Status = RegistrationStatus.Cancelled;
        BibNumber = null;
        Touch();
    }

    /// <summary>
    /// Check-in cổng/kit. Chỉ Confirmed. Quét lại = idempotent (không đổi timestamp lần đầu).
    /// </summary>
    /// <returns>true nếu vừa check-in lần đầu; false nếu đã check-in trước đó.</returns>
    public bool CheckIn(Guid staffUserId)
    {
        if (Status != RegistrationStatus.Confirmed)
        {
            throw new DomainException("Chỉ VĐV đã được duyệt (confirmed) mới check-in được.");
        }

        if (string.IsNullOrWhiteSpace(BibNumber))
        {
            throw new DomainException("Đăng ký chưa có số BIB.");
        }

        if (CheckedInAtUtc is not null)
        {
            return false;
        }

        if (staffUserId == Guid.Empty)
        {
            throw new DomainException("Thiếu người check-in.");
        }

        CheckedInAtUtc = DateTime.UtcNow;
        CheckedInByUserId = staffUserId;
        Touch();
        return true;
    }

    /// <summary>
    /// Ghi nhận finish cho VĐV. Idempotent: quét lại không ghi đè lần đầu.
    /// </summary>
    public bool Finish(DateTime finishedAtUtc, long resultSeconds)
    {
        EnsureRaceNotStopped();
        if (Status == RegistrationStatus.Finished)
        {
            return false;
        }

        if (Status != RegistrationStatus.Confirmed)
        {
            throw new DomainException("Chỉ VĐV đã được duyệt (confirmed) mới finish được.");
        }

        if (finishedAtUtc.Kind != DateTimeKind.Utc)
        {
            finishedAtUtc = DateTime.SpecifyKind(finishedAtUtc, DateTimeKind.Utc);
        }

        if (resultSeconds < 0)
        {
            throw new DomainException("Kết quả finish không hợp lệ.");
        }

        FinishedAtUtc = finishedAtUtc;
        ResultSeconds = resultSeconds;
        Status = RegistrationStatus.Finished;
        RaceStatus = RaceStatus.Finished;
        Touch();
        return true;
    }

    public void SetLeaderboardResult(DateTime startedAtUtc, DateTime finishedAtUtc, long resultSeconds)
    {
        if (Status is not (RegistrationStatus.Confirmed or RegistrationStatus.Finished))
        {
            throw new DomainException("Chỉ VĐV đã được duyệt mới có thể có kết quả.");
        }

        if (resultSeconds < 0)
        {
            throw new DomainException("Kết quả finish không hợp lệ.");
        }

        if (finishedAtUtc.Kind != DateTimeKind.Utc)
        {
            finishedAtUtc = DateTime.SpecifyKind(finishedAtUtc, DateTimeKind.Utc);
        }

        StartedAtUtc = DateTime.SpecifyKind(startedAtUtc, DateTimeKind.Utc);
        FinishedAtUtc = finishedAtUtc;
        ResultSeconds = resultSeconds;
        Status = RegistrationStatus.Finished;
        RaceStatus = RaceStatus.Finished;
        Touch();
    }

    public void RemoveLeaderboardResult()
    {
        if (Status != RegistrationStatus.Finished || !ResultSeconds.HasValue)
        {
            throw new DomainException("VĐV chưa có kết quả trên bảng xếp hạng.");
        }

        FinishedAtUtc = null;
        ResultSeconds = null;
        Status = RegistrationStatus.Confirmed;
        RaceStatus = StartedAtUtc.HasValue ? RaceStatus.Running : RaceStatus.NotStarted;
        Touch();
    }

    public bool Start(DateTime startedAtUtc)
    {
        EnsureRaceNotStopped();
        if (Status is not (RegistrationStatus.Confirmed or RegistrationStatus.Finished))
        {
            throw new DomainException("Chỉ VĐV confirmed mới có thể ghi nhận xuất phát.");
        }

        // Đã quét start trước đó rồi thì bỏ qua
        if (StartedAtUtc.HasValue)
        {
            return false;
        }

        if (RaceStatus == RaceStatus.Finished)
        {
            throw new DomainException("Không thể ghi nhận xuất phát sau khi VĐV đã hoàn thành.");
        }

        if (startedAtUtc.Kind != DateTimeKind.Utc)
        {
            startedAtUtc = DateTime.SpecifyKind(startedAtUtc, DateTimeKind.Utc);
        }

        StartedAtUtc = startedAtUtc;
        RaceStatus = RaceStatus.Running;
        Touch();
        return true;
    }
    public RaceStatusChange ChangeRaceStatus(RaceStatus target, string reason, Guid actorId,
        DateTime nowUtc, DateTime? scheduledStartUtc)
    {
        if (Status is not (RegistrationStatus.Confirmed or RegistrationStatus.Finished))
            throw new DomainException("Chỉ đăng ký đã được duyệt mới được cập nhật trạng thái thi đấu.");
        if (target is not (RaceStatus.DNS or RaceStatus.DNF or RaceStatus.DQ))
            throw new DomainException("Trạng thái phải là dns, dnf hoặc dq.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000)
            throw new DomainException("Lý do bắt buộc và không được vượt quá 1000 ký tự.");
        if (actorId == Guid.Empty || nowUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Người cập nhật hoặc thời gian cập nhật không hợp lệ.");

        EnsureRaceNotStopped();
        if (target == RaceStatus.DNS && (RaceStatus != RaceStatus.NotStarted
            || StartedAtUtc.HasValue || FinishedAtUtc.HasValue || ResultSeconds.HasValue
            || Status != RegistrationStatus.Confirmed))
            throw new DomainException("Chỉ VĐV chưa xuất phát và chưa có kết quả mới được đánh DNS.");
        if (target == RaceStatus.DNS && (scheduledStartUtc is null || nowUtc < scheduledStartUtc))
            throw new DomainException("Chỉ được đánh DNS khi đã đến giờ xuất phát của cự ly/giải.");
        if (target == RaceStatus.DNF && (RaceStatus != RaceStatus.Running
            || FinishedAtUtc.HasValue || Status != RegistrationStatus.Confirmed))
            throw new DomainException("Chỉ VĐV đang chạy, chưa hoàn thành mới được đánh DNF.");

        var change = RaceStatusChange.Create(Id, RaceStatus, target, reason.Trim(), actorId, nowUtc);
        RaceStatus = target;
        UpdatedAtUtc = nowUtc;
        return change;
    }

    private void EnsureRaceNotStopped()
    {
        if (RaceStatus is RaceStatus.DNS or RaceStatus.DNF or RaceStatus.DQ)
            throw new DomainException("VĐV đã có trạng thái DNS/DNF/DQ; không thể tiếp tục hoặc đổi quyết định.");
    }

    /// <summary>Simulation starts are held in memory, so the persisted registration is still NotStarted.</summary>
    public RaceStatusChange ApplySimulationDecision(RaceStatus target, string reason, Guid actorId,
        DateTime changedAtUtc, Guid runId, string checkpointId)
    {
        if (Status != RegistrationStatus.Confirmed || RaceStatus != RaceStatus.NotStarted
            || StartedAtUtc.HasValue || FinishedAtUtc.HasValue || ResultSeconds.HasValue)
            throw new DomainException("Đăng ký không còn đủ điều kiện ghi kết quả giả lập.");
        if (target is not (RaceStatus.DNS or RaceStatus.DNF or RaceStatus.DQ)
            || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000
            || actorId == Guid.Empty || runId == Guid.Empty || changedAtUtc.Kind != DateTimeKind.Utc
            || string.IsNullOrWhiteSpace(checkpointId) || checkpointId.Length > 120)
            throw new DomainException("Quyết định giả lập không hợp lệ.");
        var change = RaceStatusChange.Create(Id, RaceStatus, target, reason.Trim(), actorId, changedAtUtc);
        change.SetSimulationSource(runId, checkpointId);
        RaceStatus = target;
        UpdatedAtUtc = DateTime.UtcNow;
        return change;
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}

public sealed record RegistrationCreatedDomainEvent(Guid RegistrationId, Guid RaceEventId, Guid UserId) : DomainEvent;

public sealed record RegistrationConfirmedDomainEvent(Guid RegistrationId, Guid RaceEventId, Guid UserId) : DomainEvent;
