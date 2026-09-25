using MediatR;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.Application.Users.GetUser;

public sealed record GetUserByIdQuery(Guid UserId) : IRequest<UserDto>;

public sealed class GetUserByIdQueryHandler(IUserRepository users, IRoleRepository roles)
    : IRequestHandler<GetUserByIdQuery, UserDto>
{
    public async Task<UserDto> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdWithProfileAsync(request.UserId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy người dùng.");
        var role = await roles.GetByIdAsync(user.RoleId, cancellationToken);
        return UserDtoMapper.ToDto(user, role?.RoleName);
    }
}
