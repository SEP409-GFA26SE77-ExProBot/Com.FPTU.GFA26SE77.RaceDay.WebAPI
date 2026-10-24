using FluentValidation;
using MediatR;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.Application.Users.LockUser;

public sealed record LockUserCommand(Guid UserId) : IRequest;

public sealed class LockUserCommandValidator : AbstractValidator<LockUserCommand>
{
    public LockUserCommandValidator() => RuleFor(x => x.UserId).NotEmpty();
}

public sealed class LockUserCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    ICurrentUserAccessor currentUser,
    IUnitOfWork unitOfWork,
    IEventBus eventBus) : IRequestHandler<LockUserCommand>
{
    public async Task Handle(LockUserCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new DomainException("Không xác định được người thực hiện.");

        if (actorId == request.UserId)
            throw new DomainException("Không thể khóa chính tài khoản đang đăng nhập.");

        var target = await users.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy người dùng.");

        var actor = await users.GetByIdAsync(actorId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy tài khoản đang đăng nhập.");

        var actorRole = await roles.GetByIdAsync(actor.RoleId, cancellationToken);
        var targetRole = await roles.GetByIdAsync(target.RoleId, cancellationToken);

        if (!RolePrivilege.CanManage(actorRole?.RoleName, targetRole?.RoleName))
            throw new DomainException("Không có quyền khóa tài khoản ngang cấp hoặc cao hơn.");

        target.Lock();
        users.Update(target);

        await eventBus.PublishAsync(
            new UserLockedIntegrationEvent(target.Id, target.Email),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
