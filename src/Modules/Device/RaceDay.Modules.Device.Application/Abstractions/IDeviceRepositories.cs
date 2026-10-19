using RaceDay.Modules.Device.Domain.Auth;
using RaceDay.Modules.Device.Domain.Sos;

namespace RaceDay.Modules.Device.Application.Abstractions;

public interface IDeviceAuthRequestRepository
{
    Task AddAsync(DeviceAuthRequest request, CancellationToken cancellationToken = default);
    Task<DeviceAuthRequest?> GetByDeviceCodeHashAsync(string hash, CancellationToken cancellationToken = default);
    Task<DeviceAuthRequest?> GetActiveByUserCodeAsync(string userCode, CancellationToken cancellationToken = default);
    void Update(DeviceAuthRequest request);
}

public interface ILinkedDeviceRepository
{
    Task AddAsync(LinkedDevice device, CancellationToken cancellationToken = default);
    Task<LinkedDevice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LinkedDevice>> ListByRunnerAsync(Guid runnerUserId, CancellationToken cancellationToken = default);
    void Update(LinkedDevice device);
}

public interface ISosAlertRepository
{
    Task<SosAlert?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(SosAlert alert, CancellationToken cancellationToken = default);
    Task AddLocationsAsync(IReadOnlyList<SosLocation> locations, CancellationToken cancellationToken = default);
    void Update(SosAlert alert);
    Task<IReadOnlyList<SosAlert>> ListByEventAsync(
        Guid eventId,
        SosStatus? status,
        int take,
        CancellationToken cancellationToken = default);
}

public interface IDeviceUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IWearTokenService
{
    WearTokenPair Issue(LinkedDevice device);
}

public interface IDeviceFrontendOptions
{
    string VerificationBaseUrl { get; }
}

public sealed record WearTokenPair(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    string Scope);
