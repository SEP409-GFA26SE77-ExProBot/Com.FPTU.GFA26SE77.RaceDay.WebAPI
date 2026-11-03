using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Domain.Abstractions;

namespace RaceDay.Modules.RaceMonitoring.Domain.Contingency;

/// <summary>
/// Contingency plan for an event. EventId refers to event.race_events and CreatedBy refers to iam.users.
/// Neither identifier has a cross-schema foreign key.
/// </summary>
public sealed class ContingencyPlan : AggregateRoot
{
    public Guid EventId { get; private set; }
    public ContingencyPlanType PlanType { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string? TriggerCondition { get; private set; }
    public ContingencyPlanStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }

    private ContingencyPlan()
    {
    }

    public static ContingencyPlan Create(
        Guid eventId,
        ContingencyPlanType planType,
        string name,
        string description,
        ContingencyPlanStatus status,
        DateTime createdAtUtc,
        string? triggerCondition = null,
        Guid? createdBy = null)
    {
        CoordinatorInvariants.RequiredId(eventId, "Tham chiếu sự kiện không hợp lệ.");
        CoordinatorInvariants.OptionalId(createdBy, "Tham chiếu người dùng không hợp lệ.");

        return new ContingencyPlan
        {
            EventId = eventId,
            PlanType = CoordinatorInvariants.RequiredEnum(planType, "Loại kế hoạch dự phòng không hợp lệ."),
            Name = CoordinatorInvariants.RequiredText(
                name,
                CoordinatorFieldLengths.Name,
                "Tên kế hoạch dự phòng không được để trống.",
                "Tên kế hoạch dự phòng tối đa 200 ký tự."),
            Description = CoordinatorInvariants.RequiredLongText(description, "Mô tả kế hoạch dự phòng không được để trống."),
            TriggerCondition = CoordinatorInvariants.OptionalLongText(triggerCondition),
            Status = CoordinatorInvariants.RequiredEnum(status, "Trạng thái kế hoạch dự phòng không hợp lệ."),
            CreatedAtUtc = CoordinatorInvariants.RequiredTimestamp(createdAtUtc, "Thời điểm tạo kế hoạch không hợp lệ."),
            CreatedBy = createdBy
        };
    }
}
