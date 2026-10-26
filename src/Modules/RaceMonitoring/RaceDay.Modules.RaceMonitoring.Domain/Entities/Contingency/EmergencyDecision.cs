using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Domain.Abstractions;

namespace RaceDay.Modules.RaceMonitoring.Domain.Contingency;

/// <summary>
/// Audit record of an emergency decision. DecidedBy refers to iam.users with no cross-schema foreign key.
/// Recording a decision does not cancel, postpone, or notify.
/// </summary>
public sealed class EmergencyDecision : AggregateRoot
{
    public Guid ContingencyIncidentId { get; private set; }
    public ContingencyIncident? ContingencyIncident { get; private set; }
    public EmergencyDecisionType DecisionType { get; private set; }
    public string DecisionDetails { get; private set; } = string.Empty;
    public Guid DecidedBy { get; private set; }
    public DateTime DecidedAtUtc { get; private set; }
    public DateTime? EffectiveAtUtc { get; private set; }

    private EmergencyDecision()
    {
    }

    public static EmergencyDecision Create(
        Guid contingencyIncidentId,
        EmergencyDecisionType decisionType,
        string decisionDetails,
        Guid decidedBy,
        DateTime decidedAtUtc,
        DateTime? effectiveAtUtc = null)
    {
        CoordinatorInvariants.RequiredId(contingencyIncidentId, "Tham chiếu sự cố không hợp lệ.");
        CoordinatorInvariants.RequiredId(decidedBy, "Tham chiếu người quyết định không hợp lệ.");

        return new EmergencyDecision
        {
            ContingencyIncidentId = contingencyIncidentId,
            DecisionType = CoordinatorInvariants.RequiredEnum(decisionType, "Loại quyết định không hợp lệ."),
            DecisionDetails = CoordinatorInvariants.RequiredLongText(decisionDetails, "Nội dung quyết định không được để trống."),
            DecidedBy = decidedBy,
            DecidedAtUtc = CoordinatorInvariants.RequiredTimestamp(decidedAtUtc, "Thời điểm quyết định không hợp lệ."),
            EffectiveAtUtc = CoordinatorInvariants.OptionalTimestamp(effectiveAtUtc, "Thời điểm hiệu lực không hợp lệ.")
        };
    }
}
