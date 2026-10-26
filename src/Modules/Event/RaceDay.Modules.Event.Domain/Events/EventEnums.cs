namespace RaceDay.Modules.Event.Domain.Events;

public enum RaceEventStatus
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    Rejected = 3,
    RegistrationOpen = 4,
    Closed = 5,
    Completed = 6,
    Cancelled = 7,
    /// <summary>Khóa tự động khi hết hạn gói tổ chức.</summary>
    Suspended = 8
}

public enum CheckpointType
{
    Start = 0,
    Finish = 1,
    Water = 2,
    Aid = 3,
    Medical = 4,
    Cutoff = 5
}

public enum RegistrationStatus
{
    PendingPayment = 0,
    PendingApproval = 1,
    Confirmed = 2,
    // Legacy persisted value, migrated to Confirmed + RunnerRaceStatus.Finished.
    Finished = 3,
    Rejected = 4,
    Cancelled = 5
}
