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
        return Set.FirstOrDefaultAsync(x => x.Email == normalized, cancellationToken);
    }
}
