using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Device.Application.Abstractions;
using RaceDay.Modules.Device.Domain.Auth;
using RaceDay.Modules.Device.Domain.Sos;
using RaceDay.Modules.Device.Infrastructure.Persistence;

namespace RaceDay.Modules.Device.Infrastructure.Repositories;

public sealed class DeviceAuthRequestRepository(DeviceDbContext db) : IDeviceAuthRequestRepository
{
    public async Task AddAsync(DeviceAuthRequest request, CancellationToken cancellationToken = default)
        => await db.DeviceAuthRequests.AddAsync(request, cancellationToken);

    public Task<DeviceAuthRequest?> GetByDeviceCodeHashAsync(
        string hash,
        CancellationToken cancellationToken = default)
        => db.DeviceAuthRequests.FirstOrDefaultAsync(x => x.DeviceCodeHash == hash, cancellationToken);

    public Task<DeviceAuthRequest?> GetActiveByUserCodeAsync(
        string userCode,
        CancellationToken cancellationToken = default)
        => db.DeviceAuthRequests
            .Where(x => x.UserCode == userCode && !x.Consumed)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public void Update(DeviceAuthRequest request) => db.DeviceAuthRequests.Update(request);
}

public sealed class LinkedDeviceRepository(DeviceDbContext db) : ILinkedDeviceRepository
{
    public async Task AddAsync(LinkedDevice device, CancellationToken cancellationToken = default)
        => await db.LinkedDevices.AddAsync(device, cancellationToken);

    public Task<LinkedDevice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.LinkedDevices.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<LinkedDevice>> ListByRunnerAsync(
        Guid runnerUserId,
        CancellationToken cancellationToken = default)
        => await db.LinkedDevices
            .Where(x => x.RunnerUserId == runnerUserId)
            .OrderByDescending(x => x.LinkedAtUtc)
            .ToListAsync(cancellationToken);

    public void Update(LinkedDevice device) => db.LinkedDevices.Update(device);
}

public sealed class SosAlertRepository(DeviceDbContext db) : ISosAlertRepository
{
    public Task<SosAlert?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.SosAlerts.Include(x => x.Locations).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task AddAsync(SosAlert alert, CancellationToken cancellationToken = default)
        => await db.SosAlerts.AddAsync(alert, cancellationToken);

    public async Task AddLocationsAsync(
        IReadOnlyList<SosLocation> locations,
        CancellationToken cancellationToken = default)
        => await db.SosLocations.AddRangeAsync(locations, cancellationToken);

    public void Update(SosAlert alert) => db.SosAlerts.Update(alert);

    public async Task<IReadOnlyList<SosAlert>> ListByEventAsync(
        Guid eventId,
        SosStatus? status,
        int take,
        CancellationToken cancellationToken = default)
    {
        var q = db.SosAlerts.AsNoTracking()
            .Include(x => x.Locations)
            .Where(x => x.EventId == eventId);
        if (status is SosStatus s)
        {
            q = q.Where(x => x.Status == s);
        }

        return await q.OrderByDescending(x => x.CreatedAtUtc).Take(take).ToListAsync(cancellationToken);
    }
}

public sealed class DeviceUnitOfWork(DeviceDbContext db) : IDeviceUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
