using FluentValidation;
using MediatR;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.Application.Auth.ChangePassword;

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : IRequest;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(100);
    }
}

public sealed class ChangePasswordCommandHandler(
    ICurrentUserAccessor currentUser,
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    IEventBus eventBus) : IRequestHandler<ChangePasswordCommand>
{
    public async Task Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new DomainException("Bạn cần đăng nhập để đổi mật khẩu.");

        var user = await users.GetByIdAsync(userId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy người dùng.");

        if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw new DomainException("Mật khẩu hiện tại không đúng.");
        }

        user.SetPasswordHash(passwordHasher.Hash(request.NewPassword));
        users.Update(user);

        await eventBus.PublishAsync(new PasswordResetIntegrationEvent(user.Id, user.Email), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
