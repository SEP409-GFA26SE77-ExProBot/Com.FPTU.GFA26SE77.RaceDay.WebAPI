using MediatR;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.Application.Users.DeleteUser;

public sealed record DeleteUserCommand(Guid UserId) : IRequest;

public sealed class DeleteUserCommandHandler(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IEventBus eventBus) : IRequestHandler<DeleteUserCommand>
{
    public async Task Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy người dùng.");

        var email = user.Email;
        users.Remove(user);

        await eventBus.PublishAsync(new UserDeletedIntegrationEvent(request.UserId, email), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
