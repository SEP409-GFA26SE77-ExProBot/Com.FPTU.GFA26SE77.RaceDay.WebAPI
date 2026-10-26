using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Domain.Abstractions;

namespace RaceDay.Modules.RaceMonitoring.Domain.RunnerMonitoring;

/// <summary>
/// Append-only change of a runner's race-day status.
/// RegistrationId refers to event.event_registrations; ChangedBy refers to iam.users.
/// Neither identifier has a cross-schema foreign key.
/// A confirmed DNF is a transition whose <see cref="NewStatus"/> is <see cref="RunnerRaceStatus.DNF"/>.
/// </summary>
public sealed class RunnerStatusHistory : AggregateRoot
{
    public Guid RegistrationId { get; private set; }
    public RunnerRaceStatus? PreviousStatus { get; private set; }
    public RunnerRaceStatus NewStatus { get; private set; }
    public DateTime ChangedAtUtc { get; private set; }
    public Guid? ChangedBy { get; private set; }
    public string? Reason { get; private set; }

    private RunnerStatusHistory()
    {
    }

    public static RunnerStatusHistory Create(
        Guid registrationId,
        RunnerRaceStatus? previousStatus,
        RunnerRaceStatus newStatus,
        DateTime changedAtUtc,
        Guid? changedBy = null,
        string? reason = null)
    {
        CoordinatorInvariants.RequiredId(registrationId, "Tham chiếu đăng ký không hợp lệ.");
        CoordinatorInvariants.OptionalId(changedBy, "Tham chiếu người dùng không hợp lệ.");

        return new RunnerStatusHistory
        {
            RegistrationId = registrationId,
            PreviousStatus = CoordinatorInvariants.OptionalEnum(previousStatus, "Trạng thái runner trước đó không hợp lệ."),
            NewStatus = CoordinatorInvariants.RequiredEnum(newStatus, "Trạng thái runner mới không hợp lệ."),
            ChangedAtUtc = CoordinatorInvariants.RequiredTimestamp(changedAtUtc, "Thời điểm đổi trạng thái runner không hợp lệ."),
            ChangedBy = changedBy,
            Reason = CoordinatorInvariants.OptionalLongText(reason)
        };
    }
}
