using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.Iam.Application.Abstractions;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
}
