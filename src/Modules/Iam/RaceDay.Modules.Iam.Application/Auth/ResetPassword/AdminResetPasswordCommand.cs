using FluentValidation;
using MediatR;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.Application.Auth.ResetPassword;

public sealed record AdminResetPasswordCommand(Guid UserId, string NewPassword) : IRequest;

public sealed class AdminResetPasswordCommandValidator : AbstractValidator<AdminResetPasswordCommand>
{
    public AdminResetPasswordCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(100);
    }
}

public sealed class AdminResetPasswordCommandHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    IEventBus eventBus) : IRequestHandler<AdminResetPasswordCommand>
{
    public async Task Handle(AdminResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy người dùng.");

        user.SetPasswordHash(passwordHasher.Hash(request.NewPassword));
        users.Update(user);

        await eventBus.PublishAsync(new PasswordResetIntegrationEvent(user.Id, user.Email), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
