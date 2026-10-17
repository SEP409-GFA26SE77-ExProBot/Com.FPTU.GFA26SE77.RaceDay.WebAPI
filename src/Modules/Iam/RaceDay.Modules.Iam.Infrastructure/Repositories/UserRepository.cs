using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Modules.Iam.Infrastructure.Persistence;
using RaceDay.Shared.Infrastructure.Persistence.Ef;

namespace RaceDay.Modules.Iam.Infrastructure.Repositories;

public sealed class UserRepository(IamDbContext dbContext)
    : EfRepository<User, IamDbContext>(dbContext), IUserRepository
{
    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return Set.Include(x => x.Profile)
            .FirstOrDefaultAsync(x => x.Email == normalized, cancellationToken);
    }

    public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var normalized = username.Trim().ToLowerInvariant();
        return Set.Include(x => x.Profile)
            .FirstOrDefaultAsync(x => x.Username == normalized, cancellationToken);
    }

    public Task<User?> GetByIdWithProfileAsync(Guid id, CancellationToken cancellationToken = default)
        => Set.Include(x => x.Profile).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<User> Items, int Total)> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = Set.AsNoTracking().Include(x => x.Profile).OrderBy(x => x.FullName);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }
}
