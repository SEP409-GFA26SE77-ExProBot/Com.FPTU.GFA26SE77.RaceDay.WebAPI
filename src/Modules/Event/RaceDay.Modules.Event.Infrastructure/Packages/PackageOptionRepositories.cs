using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Packages;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Packages;

public sealed class PackageDurationOptionRepository(EventDbContext db) : IPackageDurationOptionRepository
{
    public Task<PackageDurationOption?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.PackageDurationOptions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<PackageDurationOption?> GetActiveByMonthsAsync(int months, CancellationToken cancellationToken = default)
        => db.PackageDurationOptions.FirstOrDefaultAsync(
            x => x.Months == months && x.Status == PackageOptionStatus.Active, cancellationToken);

    public async Task<IReadOnlyList<PackageDurationOption>> ListAllAsync(CancellationToken cancellationToken = default)
        => await db.PackageDurationOptions.OrderBy(x => x.SortOrder).ThenBy(x => x.Months).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PackageDurationOption>> ListActiveAsync(CancellationToken cancellationToken = default)
        => await db.PackageDurationOptions
            .Where(x => x.Status == PackageOptionStatus.Active)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Months)
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsByMonthsAsync(int months, CancellationToken cancellationToken = default)
        => db.PackageDurationOptions.AnyAsync(x => x.Months == months, cancellationToken);

    public async Task AddAsync(PackageDurationOption option, CancellationToken cancellationToken = default)
        => await db.PackageDurationOptions.AddAsync(option, cancellationToken);

    public void Update(PackageDurationOption option) => db.PackageDurationOptions.Update(option);
}

public sealed class PackageCapacityOptionRepository(EventDbContext db) : IPackageCapacityOptionRepository
{
    public Task<PackageCapacityOption?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.PackageCapacityOptions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<PackageCapacityOption?> GetActiveByCapAsync(int maxParticipants, CancellationToken cancellationToken = default)
        => db.PackageCapacityOptions.FirstOrDefaultAsync(
            x => x.MaxParticipants == maxParticipants && x.Status == PackageOptionStatus.Active, cancellationToken);

    public async Task<IReadOnlyList<PackageCapacityOption>> ListAllAsync(CancellationToken cancellationToken = default)
        => await db.PackageCapacityOptions.OrderBy(x => x.SortOrder).ThenBy(x => x.MaxParticipants).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PackageCapacityOption>> ListActiveAsync(CancellationToken cancellationToken = default)
        => await db.PackageCapacityOptions
            .Where(x => x.Status == PackageOptionStatus.Active)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.MaxParticipants)
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsByCapAsync(int maxParticipants, CancellationToken cancellationToken = default)
        => db.PackageCapacityOptions.AnyAsync(x => x.MaxParticipants == maxParticipants, cancellationToken);

    public async Task AddAsync(PackageCapacityOption option, CancellationToken cancellationToken = default)
        => await db.PackageCapacityOptions.AddAsync(option, cancellationToken);

    public void Update(PackageCapacityOption option) => db.PackageCapacityOptions.Update(option);
}
