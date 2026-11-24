namespace RaceDay.RfidGateway.Timing;

/// <summary>
/// Contract chung của adapter YPD-202 và nguồn kiểm thử trước khi gửi lên backend.
/// </summary>
public sealed record TimingRead(
    Guid ReadId,
    string ReaderId,
    string TagScheme,
    string TagId,
    DateTime ObservedAtUtc,
    string TimestampSource);

public static class TimestampSources
{
    public const string GatewayClock = "GatewayClock";
    public const string TestClock = "TestClock";
}
