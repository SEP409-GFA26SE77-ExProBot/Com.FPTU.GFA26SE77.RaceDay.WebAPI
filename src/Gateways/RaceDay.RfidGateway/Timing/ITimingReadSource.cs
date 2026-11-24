namespace RaceDay.RfidGateway.Timing;

public interface ITimingReadSource
{
    IAsyncEnumerable<TimingRead> ReadAsync(CancellationToken cancellationToken);
}
