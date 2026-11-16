using RaceDay.Modules.Positioning.Application;
using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.Positioning.Infrastructure;

public sealed class CachedPositionTracker(ICacheService cache) : IPositionTracker, IDisposable
{
    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1)).ToArray();

    public async Task<PositionRecordResult> RecordAsync(Guid eventId, Guid userId, PositionSample sample, bool? isOutside, CancellationToken ct)
    {
        var key = PositionKey(eventId, userId);
        var gate = _locks[(uint)HashCode.Combine(eventId, userId) % _locks.Length];
        await gate.WaitAsync(ct);
        try
        {
            var previous = await cache.GetAsync<LatestPosition>(key, ct);
            if (previous is not null && previous.Sample.CapturedAtUtc >= sample.CapturedAtUtc) return new(false, false);

            var current = new LatestPosition(sample, isOutside ?? previous?.Outside);
            await cache.SetAsync(key, current, TimeSpan.FromMinutes(2), ct);
            return new(true, isOutside == true && previous?.Outside != true);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PositionSample?> GetLatestAsync(Guid eventId, Guid userId, CancellationToken ct)
        => (await cache.GetAsync<LatestPosition>(PositionKey(eventId, userId), ct))?.Sample;

    private static string PositionKey(Guid eventId, Guid userId) => $"positioning:{eventId:N}:{userId:N}";

    public void Dispose()
    {
        foreach (var gate in _locks) gate.Dispose();
    }

    private sealed record LatestPosition(PositionSample Sample, bool? Outside);
}
