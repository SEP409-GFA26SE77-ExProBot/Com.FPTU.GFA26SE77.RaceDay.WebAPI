using System.Runtime.CompilerServices;
using RaceDay.RfidGateway.Timing;

namespace RaceDay.RfidGateway.Ypd202;

public sealed class Ypd202TimingSource : ITimingReadSource
{
    private readonly Func<CancellationToken, Task<IYpd202Session>> _connect;
    private readonly string _readerId;
    private readonly TimeSpan _pollInterval;
    private readonly int? _maxPolls;
    private readonly Action<string> _log;

    public Ypd202TimingSource(
        Func<CancellationToken, Task<IYpd202Session>> connect,
        string readerId,
        TimeSpan pollInterval,
        int? maxPolls,
        Action<string>? log = null)
    {
        _connect = connect;
        _readerId = readerId;
        _pollInterval = pollInterval;
        _maxPolls = maxPolls;
        _log = log ?? Console.WriteLine;
    }

    public static Ypd202TimingSource ForReader(
        string host,
        int port,
        string readerId,
        TimeSpan pollInterval,
        int? maxPolls,
        Action<string>? log = null)
        => new(
            async cancellationToken => await Ypd202Reader.ConnectAsync(host, port, cancellationToken),
            readerId,
            pollInterval,
            maxPolls,
            log);

    public async IAsyncEnumerable<TimingRead> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IYpd202Session? session = null;
        try
        {
            var polls = 0;
            while (!cancellationToken.IsCancellationRequested && (_maxPolls is null || polls < _maxPolls))
            {
                var outcome = await PollAsync(session, cancellationToken);
                session = outcome.Session;
                if (!outcome.Success)
                {
                    if (_maxPolls is not null)
                    {
                        throw outcome.Error ?? new IOException("Không đọc được đầu đọc YPD-202.");
                    }

                    _log($"Mất kết nối đầu đọc: {outcome.Error?.Message}. Thử lại sau 2 giây.");
                    session = null;
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                polls++;
                var result = outcome.Result!;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var emitted = 0;
                foreach (var observation in result.Observations)
                {
                    if (!seen.Add(observation.Epc))
                    {
                        continue;
                    }

                    emitted++;
                    _log($"EPC={observation.Epc} RSSI=0x{observation.RssiRaw:X2}");
                    var observedAtUtc = observation.ObservedAt.UtcDateTime;
                    if (observedAtUtc.Kind != DateTimeKind.Utc)
                    {
                        observedAtUtc = DateTime.SpecifyKind(observedAtUtc, DateTimeKind.Utc);
                    }

                    yield return new TimingRead(
                        Guid.NewGuid(),
                        _readerId,
                        "EPC",
                        observation.Epc,
                        observedAtUtc,
                        TimestampSources.GatewayClock);
                }

                _log(emitted == 0
                    ? $"Poll {polls} [{result.CompletionStatus}] không thấy EPC."
                    : $"Poll {polls} [{result.CompletionStatus}] {emitted} EPC.");

                if (_maxPolls is null || polls < _maxPolls)
                {
                    await Task.Delay(_pollInterval, cancellationToken);
                }
            }
        }
        finally
        {
            session?.Dispose();
        }
    }

    private async Task<PollOutcome> PollAsync(IYpd202Session? session, CancellationToken cancellationToken)
    {
        try
        {
            session ??= await _connect(cancellationToken);
            var result = await session.InventoryOnceAsync(cancellationToken);
            return PollOutcome.Ok(session, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            session?.Dispose();
            return PollOutcome.Fail(ex);
        }
    }

    private sealed record PollOutcome(
        bool Success,
        IYpd202Session? Session,
        Ypd202InventoryResult? Result,
        Exception? Error)
    {
        public static PollOutcome Ok(IYpd202Session session, Ypd202InventoryResult result)
            => new(true, session, result, null);

        public static PollOutcome Fail(Exception error) => new(false, null, null, error);
    }
}
