using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Persistence;

public sealed class EventUnitOfWork(EventDbContext dbContext) : IEventUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => dbContext.SaveChangesAsync(cancellationToken);
}
