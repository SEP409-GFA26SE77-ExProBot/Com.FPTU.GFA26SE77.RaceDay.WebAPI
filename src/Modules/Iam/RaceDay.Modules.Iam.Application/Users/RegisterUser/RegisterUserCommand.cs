using MediatR;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.Application.Users.RegisterUser;

public sealed record RegisterUserCommand(string Email, string Password) : IRequest<Guid>;

public sealed class RegisterUserCommandHandler(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IEventBus eventBus) : IRequestHandler<RegisterUserCommand, Guid>
{
    public async Task<Guid> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
    {
        var existing = await users.GetByEmailAsync(request.Email, cancellationToken);
        if (existing is not null)
        {
            throw new DomainException("Email is already registered.");
        }

        // Scaffold: replace with real password hasher in Infrastructure
        var user = User.Create(request.Email, $"hash:{request.Password}");
        await users.AddAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await eventBus.PublishAsync(
            new UserCreatedIntegrationEvent(user.Id, user.Email),
            cancellationToken);

        return user.Id;
    }
}
