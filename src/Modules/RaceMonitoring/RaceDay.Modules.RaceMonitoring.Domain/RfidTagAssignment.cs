using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Domain;

/// <summary>
/// Gán EPC cho một đăng ký trong một giải. Không có khóa ngoại sang schema event.
/// </summary>
public sealed class RfidTagAssignment : AggregateRoot
{
    public const string EpcScheme = "EPC";

    public Guid EventId { get; private set; }
    public Guid RegistrationId { get; private set; }
    public string TagScheme { get; private set; } = "";
    public string TagId { get; private set; } = "";
    public Guid AssignedByUserId { get; private set; }
    public DateTime AssignedAtUtc { get; private set; }

    private RfidTagAssignment()
    {
    }

    public static RfidTagAssignment Create(
        Guid eventId,
        Guid registrationId,
        string normalizedTagId,
        Guid assignedByUserId,
        DateTime assignedAtUtc)
    {
        if (eventId == Guid.Empty)
        {
            throw new DomainException("Tham chiếu giải không hợp lệ.");
        }

        if (registrationId == Guid.Empty)
        {
            throw new DomainException("Tham chiếu đăng ký không hợp lệ.");
        }

        if (assignedByUserId == Guid.Empty)
        {
            throw new DomainException("Người gán thẻ không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(normalizedTagId) || normalizedTagId.Length > 128)
        {
            throw new DomainException("EPC không hợp lệ.");
        }

        if (assignedAtUtc == default)
        {
            throw new DomainException("Thời điểm gán thẻ không hợp lệ.");
        }

        return new RfidTagAssignment
        {
            EventId = eventId,
            RegistrationId = registrationId,
            TagScheme = EpcScheme,
            TagId = normalizedTagId,
            AssignedByUserId = assignedByUserId,
            AssignedAtUtc = assignedAtUtc.Kind == DateTimeKind.Utc
                ? assignedAtUtc
                : DateTime.SpecifyKind(assignedAtUtc, DateTimeKind.Utc)
        };
    }
}
