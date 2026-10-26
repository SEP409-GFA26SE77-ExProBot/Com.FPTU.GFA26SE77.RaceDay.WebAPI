using MediatR;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.Application.Users.GetUser;

public sealed record GetMyProfileQuery : IRequest<UserDto>;

public sealed class GetMyProfileQueryHandler(
    ICurrentUserAccessor currentUser,
    IUserRepository users,
    IRoleRepository roles) : IRequestHandler<GetMyProfileQuery, UserDto>
{
    public async Task<UserDto> Handle(GetMyProfileQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new DomainException("Bạn cần đăng nhập.");

        var user = await users.GetByIdWithProfileAsync(userId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy người dùng.");
        var role = await roles.GetByIdAsync(user.RoleId, cancellationToken);
        return UserDtoMapper.ToDto(user, role?.RoleName);
    }
}
