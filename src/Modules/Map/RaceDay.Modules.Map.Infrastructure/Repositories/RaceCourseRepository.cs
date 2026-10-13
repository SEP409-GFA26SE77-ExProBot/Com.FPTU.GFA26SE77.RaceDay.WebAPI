using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Map.Application.Courses;
using RaceDay.Modules.Map.Domain.Courses;
using RaceDay.Modules.Map.Infrastructure.Persistence;

namespace RaceDay.Modules.Map.Infrastructure.Repositories;

public sealed class RaceCourseRepository(MapDbContext db) : IRaceCourseRepository
{
    public Task<RaceCourse?> GetByEventDistanceAsync(Guid eventId, Guid distanceId, CancellationToken cancellationToken = default)
        => db.RaceCourses.FirstOrDefaultAsync(x => x.EventId == eventId && x.DistanceId == distanceId, cancellationToken);

    public async Task<IReadOnlyList<RaceCourse>> ListByEventAsync(Guid eventId, CancellationToken cancellationToken = default)
        => await db.RaceCourses.Where(x => x.EventId == eventId).ToListAsync(cancellationToken);

    public async Task AddAsync(RaceCourse course, CancellationToken cancellationToken = default)
        => await db.RaceCourses.AddAsync(course, cancellationToken);

    public void Update(RaceCourse course) => db.RaceCourses.Update(course);
}

public sealed class MapUnitOfWork(MapDbContext db) : IMapUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
