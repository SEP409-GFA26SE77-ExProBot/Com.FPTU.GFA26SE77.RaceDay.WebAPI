namespace RaceDay.Shared.Application.Abstractions;

/// <summary>
/// Use with EF Core / PostgreSQL transactional modules (e.g. IAM).
/// MongoDB modules typically do not need a classic Unit of Work.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
