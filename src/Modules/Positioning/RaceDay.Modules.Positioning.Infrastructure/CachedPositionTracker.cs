using RaceDay.Modules.Positioning.Application;
using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.Positioning.Infrastructure;

public sealed class CachedPositionTracker(ICacheService cache) : IPositionTracker, IDisposable
{
    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1)).ToArray();

    public async Task<bool> RecordAsync(Guid eventId, Guid userId, PositionSample sample, bool? isOutside, CancellationToken ct)
    {
        var key = $"positioning:{eventId:N}:{userId:N}";
        var gate = _locks[(uint)HashCode.Combine(eventId, userId) % _locks.Length];
        await gate.WaitAsync(ct);
        try
        {
            var previous = await cache.GetAsync<LatestPosition>(key, ct);
            if (previous is not null && previous.Sample.CapturedAtUtc >= sample.CapturedAtUtc) return false;

            var current = new LatestPosition(sample, isOutside ?? previous?.Outside);
            await cache.SetAsync(key, current, TimeSpan.FromMinutes(2), ct);
            return isOutside == true && previous?.Outside != true;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        foreach (var gate in _locks) gate.Dispose();
    }

    private sealed record LatestPosition(PositionSample Sample, bool? Outside);
}
