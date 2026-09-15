using RaceDay.Shared.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace RaceDay.Shared.Infrastructure.Persistence.Ef;

public class EfUnitOfWork<TContext>(TContext dbContext) : IUnitOfWork
    where TContext : DbContext
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => dbContext.SaveChangesAsync(cancellationToken);
}
