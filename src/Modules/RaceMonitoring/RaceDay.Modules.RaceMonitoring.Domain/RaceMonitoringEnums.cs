namespace RaceDay.Modules.RaceMonitoring.Domain;

public enum ScanMethod
{
    RFID = 0,
    Manual = 1
}

/// <summary>Trạng thái thi đấu do điều phối ghi nhận. Khác RegistrationStatus của module Event.</summary>
public enum RunnerRaceStatus
{
    NotStarted = 0,
    OnCourse = 1,
    Finished = 2,
    DNF = 3,
    DNS = 4,
    Disqualified = 5
}

public enum RouteDeviationSeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public enum RouteDeviationStatus
{
    Open = 0,
    Acknowledged = 1,
    Resolved = 2
}

public enum EmergencyIncidentType
{
    Medical = 0,
    Trauma = 1,
    Cardiac = 2,
    Heat = 3,
    Other = 4
}

public enum EmergencyPriority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public enum EmergencyResponseStatus
{
    Open = 0,
    Assigned = 1,
    InProgress = 2,
    Resolved = 3,
    Cancelled = 4
}

public enum EmergencyResourceType
{
    MedicalTeam = 0,
    Ambulance = 1,
    FirstAid = 2,
    Other = 3
}

public enum EmergencyResourceStatus
{
    Available = 0,
    Assigned = 1,
    Unavailable = 2,
    Offline = 3
}

public enum EmergencyAssignmentStatus
{
    Assigned = 0,
    Accepted = 1,
    Arrived = 2,
    Completed = 3,
    Cancelled = 4
}

public enum AidStationStatus
{
    Open = 0,
    Closed = 1,
    Limited = 2
}

public enum SupplyTransactionType
{
    Restock = 0,
    Issue = 1,
    Adjustment = 2,
    Transfer = 3
}

public enum ContingencyPlanType
{
    Weather = 0,
    RouteClosure = 1,
    MedicalSurge = 2,
    Security = 3,
    Other = 4
}

public enum ContingencyPlanStatus
{
    Draft = 0,
    Active = 1,
    Retired = 2
}

public enum ContingencyIncidentType
{
    Weather = 0,
    CourseHazard = 1,
    Medical = 2,
    Security = 3,
    Logistics = 4,
    Other = 5
}

public enum ContingencySeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public enum ContingencyIncidentStatus
{
    Open = 0,
    Monitoring = 1,
    Resolved = 2,
    Cancelled = 3
}

/// <summary>Nhãn quyết định được lưu để audit. Không thực thi hủy hoặc hoãn giải.</summary>
public enum EmergencyDecisionType
{
    Continue = 0,
    Divert = 1,
    Delay = 2,
    Evacuate = 3,
    Suspend = 4,
    Cancel = 5,
    Other = 6
}

public enum BroomWagonStatus
{
    Standby = 0,
    EnRoute = 1,
    Completed = 2,
    OutOfService = 3
}
