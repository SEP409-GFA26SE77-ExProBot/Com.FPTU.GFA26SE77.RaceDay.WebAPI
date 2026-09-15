using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace RaceDay.Shared.Infrastructure.Persistence.Ef;

public class EfRepository<TEntity, TContext>(TContext dbContext) : IRepository<TEntity>
    where TEntity : Entity
    where TContext : DbContext
{
    protected TContext DbContext { get; } = dbContext;
    protected DbSet<TEntity> Set => DbContext.Set<TEntity>();

    public virtual async Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await Set.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public virtual async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
        => await Set.AddAsync(entity, cancellationToken);

    public virtual void Update(TEntity entity) => Set.Update(entity);

    public virtual void Remove(TEntity entity) => Set.Remove(entity);
}
