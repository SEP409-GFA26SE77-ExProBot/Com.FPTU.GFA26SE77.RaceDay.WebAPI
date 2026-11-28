using RaceDay.Modules.Positioning.Application;
using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.Positioning.Infrastructure;

public sealed class CachedStaffCheckInStore(ICacheService cache) : IStaffCheckInStore
{
    /// <summary>A check-in lasts the race day; toggling Check-in off on the phone ends it earlier.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    public async Task<DateTimeOffset?> GetAsync(Guid eventId, Guid userId, CancellationToken ct)
        => (await cache.GetAsync<CheckIn>(Key(eventId, userId), ct))?.CheckedInAtUtc;

    public Task SetAsync(Guid eventId, Guid userId, DateTimeOffset checkedInAtUtc, CancellationToken ct)
        => cache.SetAsync(Key(eventId, userId), new CheckIn(checkedInAtUtc), Lifetime, ct);

    public Task RemoveAsync(Guid eventId, Guid userId, CancellationToken ct) => cache.RemoveAsync(Key(eventId, userId), ct);

    private static string Key(Guid eventId, Guid userId) => $"positioning:checkin:{eventId:N}:{userId:N}";

    private sealed record CheckIn(DateTimeOffset CheckedInAtUtc);
}
