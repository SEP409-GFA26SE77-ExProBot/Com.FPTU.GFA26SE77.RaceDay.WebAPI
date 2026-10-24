namespace RaceDay.Modules.RaceMonitoring.Domain;

public enum ScanMethod
{
    RFID = 0,
    Manual = 1
}

public enum SOSAlertStatus
{
    Active = 0,
    Acknowledged = 1,
    Resolved = 2
}
