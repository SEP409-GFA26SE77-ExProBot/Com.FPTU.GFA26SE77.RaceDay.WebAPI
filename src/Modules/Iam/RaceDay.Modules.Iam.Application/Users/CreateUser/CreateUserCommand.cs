using FluentValidation;
using MediatR;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.Application.Users.CreateUser;

public sealed record CreateUserCommand(
    string Email,
    string Password,
    string FullName,
    Guid RoleId,
    string? Phone,
    DateOnly? DateOfBirth,
    GenderDto Gender,
    UserStatusDto Status) : IRequest<Guid>;

public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(100);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(100);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.Phone).MaximumLength(20).When(x => x.Phone is not null);
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Gender).IsInEnum();
    }
}

public sealed class CreateUserCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    IEventBus eventBus) : IRequestHandler<CreateUserCommand, Guid>
{
    public async Task<Guid> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        var existing = await users.GetByEmailAsync(request.Email, cancellationToken);
        if (existing is not null)
        {
            throw new DomainException("Email đã được đăng ký.");
        }

        _ = await roles.GetByIdAsync(request.RoleId, cancellationToken)
            ?? throw new DomainException("Role không tồn tại.");

        var user = User.Create(
            request.Email,
            passwordHasher.Hash(request.Password),
            request.FullName,
            request.RoleId,
            request.Phone,
            request.DateOfBirth,
            request.Gender.ToDomain(),
            request.Status.ToDomain());

        if (request.Status == UserStatusDto.Active)
        {
            user.ConfirmEmailVerification();
        }

        await users.AddAsync(user, cancellationToken);

        await eventBus.PublishAsync(
            new UserCreatedIntegrationEvent(user.Id, user.Email, user.FullName),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return user.Id;
    }
}
