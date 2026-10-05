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
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

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

    public void MarkPaidPendingApproval()
    {
        if (Status != RegistrationStatus.PendingPayment)
        {
            throw new DomainException("Đăng ký không ở trạng thái chờ thanh toán.");
        }

        Status = RegistrationStatus.PendingApproval;
        Touch();
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

    public void CancelUnpaid()
    {
        if (Status != RegistrationStatus.PendingPayment)
        {
            throw new DomainException("Chỉ hủy được đơn đang chờ thanh toán.");
        }

        Status = RegistrationStatus.Cancelled;
        Touch();
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}

public sealed record RegistrationCreatedDomainEvent(Guid RegistrationId, Guid RaceEventId, Guid UserId) : DomainEvent;

public sealed record RegistrationConfirmedDomainEvent(Guid RegistrationId, Guid RaceEventId, Guid UserId) : DomainEvent;
