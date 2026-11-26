using System.Runtime.CompilerServices;

namespace RaceDay.RfidGateway.Timing;

public sealed class ScriptedTimingSource(IReadOnlyList<TimingRead> reads) : ITimingReadSource
{
    public async IAsyncEnumerable<TimingRead> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var read in reads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return read;
            await Task.Yield();
        }
    }
}
