using MediatR;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Domain.Users;

namespace RaceDay.Modules.Iam.Application.Users.Queries;

public sealed record IsUserLockedQuery(Guid UserId) : IRequest<bool>;

public sealed class IsUserLockedQueryHandler(IUserRepository users) : IRequestHandler<IsUserLockedQuery, bool>
{
    public async Task<bool> Handle(IsUserLockedQuery request, CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
        {
            return false;
        }

        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        return user?.Status == UserStatus.Locked;
    }
}
