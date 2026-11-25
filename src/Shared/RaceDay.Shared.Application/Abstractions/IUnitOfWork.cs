namespace RaceDay.Shared.Application.Abstractions;

/// <summary>
/// Use with EF Core / PostgreSQL transactional modules (e.g. IAM).
/// Append-only AuditLog uses Postgres SaveChanges without classic UoW.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
