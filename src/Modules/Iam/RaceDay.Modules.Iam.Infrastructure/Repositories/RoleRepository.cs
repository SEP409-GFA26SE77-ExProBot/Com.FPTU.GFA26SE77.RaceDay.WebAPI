using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Domain.Roles;
using RaceDay.Modules.Iam.Infrastructure.Persistence;

namespace RaceDay.Modules.Iam.Infrastructure.Repositories;

public sealed class RoleRepository(IamDbContext dbContext) : IRoleRepository
{
    public Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => dbContext.Roles.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Role?> GetByNameAsync(string roleName, CancellationToken cancellationToken = default)
    {
        var name = roleName.Trim();
        return dbContext.Roles.FirstOrDefaultAsync(x => x.RoleName == name, cancellationToken);
    }

    public async Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken = default)
        => await dbContext.Roles.AsNoTracking().OrderBy(x => x.RoleName).ToListAsync(cancellationToken);
}
