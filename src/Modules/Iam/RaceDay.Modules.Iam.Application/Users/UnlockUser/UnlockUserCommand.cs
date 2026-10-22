using FluentValidation;
using MediatR;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.Application.Users.UnlockUser;

public sealed record UnlockUserCommand(Guid UserId) : IRequest;

public sealed class UnlockUserCommandValidator : AbstractValidator<UnlockUserCommand>
{
    public UnlockUserCommandValidator() => RuleFor(x => x.UserId).NotEmpty();
}

public sealed class UnlockUserCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    ICurrentUserAccessor currentUser,
    IUnitOfWork unitOfWork,
    IEventBus eventBus) : IRequestHandler<UnlockUserCommand>
{
    public async Task Handle(UnlockUserCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new DomainException("Không xác định được người thực hiện.");

        if (actorId == request.UserId)
            throw new DomainException("Không thể tự mở khóa theo cách này.");

        var target = await users.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy người dùng.");

        var actor = await users.GetByIdAsync(actorId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy tài khoản đang đăng nhập.");

        var actorRole = await roles.GetByIdAsync(actor.RoleId, cancellationToken);
        var targetRole = await roles.GetByIdAsync(target.RoleId, cancellationToken);

        if (!RolePrivilege.CanManage(actorRole?.RoleName, targetRole?.RoleName))
            throw new DomainException("Không có quyền mở khóa tài khoản ngang cấp hoặc cao hơn.");

        target.Unlock();
        users.Update(target);

        await eventBus.PublishAsync(
            new UserUnlockedIntegrationEvent(target.Id, target.Email),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
