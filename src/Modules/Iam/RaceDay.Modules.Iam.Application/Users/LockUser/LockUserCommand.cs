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
    IUnitOfWork unitOfWork,
    IEventBus eventBus) : IRequestHandler<LockUserCommand>
{
    public async Task Handle(LockUserCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy người dùng.");

        user.Lock();
        users.Update(user);

        await eventBus.PublishAsync(
            new UserLockedIntegrationEvent(user.Id, user.Email),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
