using FluentValidation;
using MediatR;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Application.Validation;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.Application.Users.CreateUser;

public sealed record CreateUserCommand(
    string Email,
    string Username,
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
        RuleFor(x => x.Username)
            .NotEmpty()
            .MinimumLength(User.UsernameMinLength)
            .MaximumLength(User.UsernameMaxLength)
            .Matches(@"^[a-zA-Z0-9._-]+$")
            .WithMessage("Tên đăng nhập chỉ gồm chữ, số, dấu chấm, gạch dưới hoặc gạch ngang.");
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(100);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.Phone).MaximumLength(20).OptionalVietnamPhone();
        RuleFor(x => x.DateOfBirth).DateOfBirth();
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
        var existingEmail = await users.GetByEmailAsync(request.Email, cancellationToken);
        if (existingEmail is not null)
        {
            throw new DomainException("Email đã được đăng ký.");
        }

        var existingUsername = await users.GetByUsernameAsync(request.Username, cancellationToken);
        if (existingUsername is not null)
        {
            throw new DomainException("Tên đăng nhập đã được sử dụng.");
        }

        _ = await roles.GetByIdAsync(request.RoleId, cancellationToken)
            ?? throw new DomainException("Role không tồn tại.");

        var user = User.Create(
            request.Email,
            request.Username,
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
            new UserCreatedIntegrationEvent(user.Id, user.Email, user.Username, user.FullName),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return user.Id;
    }
}
