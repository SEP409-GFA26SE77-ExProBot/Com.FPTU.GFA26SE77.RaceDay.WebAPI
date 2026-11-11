namespace RaceDay.Modules.RaceMonitoring.Application.RaceEventLogs;

public static class RaceEventLogStreamPolicy
{
    public static readonly TimeSpan ReplayOverlap = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    public const int SubscriberCapacity = 256;
    public const int DemoDefaultCount = 5;
    public const int DemoMaxCount = 20;
    public const string CreatedEventName = "RaceEventLogCreated";
    public const string OverrunEventName = "RaceEventLogStreamOverrun";
    public const string AccessRevokedEventName = "RaceEventLogAccessRevoked";
}
