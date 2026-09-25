using MediatR;
using RaceDay.Modules.Iam.Application.Abstractions;

namespace RaceDay.Modules.Iam.Application.Users.ListUsers;

public sealed record ListUsersQuery(int Page = 1, int PageSize = 20) : IRequest<PagedUsersResult>;

public sealed record PagedUsersResult(IReadOnlyList<UserDto> Items, int Total, int Page, int PageSize);

public sealed class ListUsersQueryHandler(IUserRepository users, IRoleRepository roles)
    : IRequestHandler<ListUsersQuery, PagedUsersResult>
{
    public async Task<PagedUsersResult> Handle(ListUsersQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? 20 : request.PageSize;
        var (items, total) = await users.ListAsync(page, pageSize, cancellationToken);
        var roleMap = (await roles.ListAsync(cancellationToken)).ToDictionary(r => r.Id, r => r.RoleName);
        var dtos = items.Select(u => UserDtoMapper.ToDto(u, roleMap.GetValueOrDefault(u.RoleId))).ToList();
        return new PagedUsersResult(dtos, total, page, pageSize);
    }
}
