using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Domain.RunnerMonitoring;

/// <summary>
/// Recorded route deviation. RegistrationId refers to event.event_registrations with no cross-schema foreign key.
/// DistanceFromRoute is meters. This type does not calculate or match a route.
/// </summary>
public sealed class RouteDeviation : AggregateRoot
{
    public Guid RegistrationId { get; private set; }
    public DateTime DetectedAtUtc { get; private set; }
    public decimal DistanceFromRoute { get; private set; }
    public RouteDeviationSeverity Severity { get; private set; }
    public RouteDeviationStatus Status { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public string? Notes { get; private set; }

    private RouteDeviation()
    {
    }

    public static RouteDeviation Create(
        Guid registrationId,
        DateTime detectedAtUtc,
        decimal distanceFromRoute,
        RouteDeviationSeverity severity,
        RouteDeviationStatus status,
        DateTime? resolvedAtUtc = null,
        string? notes = null)
    {
        CoordinatorInvariants.RequiredId(registrationId, "Tham chiếu đăng ký không hợp lệ.");

        if (distanceFromRoute < 0)
        {
            throw new DomainException("Khoảng cách lệch cung đường không được âm.");
        }

        return new RouteDeviation
        {
            RegistrationId = registrationId,
            DetectedAtUtc = CoordinatorInvariants.RequiredTimestamp(detectedAtUtc, "Thời điểm phát hiện lệch cung không hợp lệ."),
            DistanceFromRoute = distanceFromRoute,
            Severity = CoordinatorInvariants.RequiredEnum(severity, "Mức độ lệch cung không hợp lệ."),
            Status = CoordinatorInvariants.RequiredEnum(status, "Trạng thái lệch cung không hợp lệ."),
            ResolvedAtUtc = CoordinatorInvariants.OptionalTimestamp(resolvedAtUtc, "Thời điểm xử lý lệch cung không hợp lệ."),
            Notes = CoordinatorInvariants.OptionalLongText(notes)
        };
    }
}
