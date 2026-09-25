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
    IUnitOfWork unitOfWork,
    IEventBus eventBus) : IRequestHandler<UnlockUserCommand>
{
    public async Task Handle(UnlockUserCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy người dùng.");

        user.Unlock();
        users.Update(user);

        await eventBus.PublishAsync(
            new UserUnlockedIntegrationEvent(user.Id, user.Email),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
