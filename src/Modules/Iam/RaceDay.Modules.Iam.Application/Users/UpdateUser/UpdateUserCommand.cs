using FluentValidation;
using MediatR;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Application.Validation;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.Application.Users.UpdateUser;

public sealed record UpdateUserRequest(
    string Username,
    string FullName,
    string? Phone,
    DateOnly? DateOfBirth,
    GenderDto Gender,
    Guid RoleId,
    UserStatusDto Status,
    string? ProfileDescription,
    string? ContactEmail,
    string? ContactPhone);

public sealed record UpdateUserCommand(
    Guid UserId,
    string Username,
    string FullName,
    string? Phone,
    DateOnly? DateOfBirth,
    GenderDto Gender,
    Guid RoleId,
    UserStatusDto Status,
    string? ProfileDescription,
    string? ContactEmail,
    string? ContactPhone) : IRequest;

public sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Username)
            .NotEmpty()
            .MinimumLength(User.UsernameMinLength)
            .MaximumLength(User.UsernameMaxLength)
            .Matches(@"^[a-zA-Z0-9._-]+$")
            .WithMessage("Tên đăng nhập chỉ gồm chữ, số, dấu chấm, gạch dưới hoặc gạch ngang.");
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.Phone).MaximumLength(20).OptionalVietnamPhone();
        RuleFor(x => x.DateOfBirth).DateOfBirth();
        RuleFor(x => x.ContactPhone).MaximumLength(20).OptionalVietnamPhone();
        RuleFor(x => x.ContactEmail).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
        RuleFor(x => x.Gender).IsInEnum();
        RuleFor(x => x.Status).IsInEnum();
    }
}

public sealed class UpdateUserCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    IUnitOfWork unitOfWork,
    IEventBus eventBus) : IRequestHandler<UpdateUserCommand>
{
    public async Task Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdWithProfileAsync(request.UserId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy người dùng.");

        _ = await roles.GetByIdAsync(request.RoleId, cancellationToken)
            ?? throw new DomainException("Role không tồn tại.");

        var normalizedUsername = User.NormalizeUsername(request.Username);
        if (!string.Equals(user.Username, normalizedUsername, StringComparison.Ordinal))
        {
            var taken = await users.GetByUsernameAsync(normalizedUsername, cancellationToken);
            if (taken is not null && taken.Id != user.Id)
            {
                throw new DomainException("Tên đăng nhập đã được sử dụng.");
            }

            user.ChangeUsername(normalizedUsername);
        }

        user.UpdateProfile(
            request.FullName,
            request.Phone,
            request.DateOfBirth,
            request.Gender.ToDomain(),
            request.ProfileDescription,
            request.ContactEmail,
            request.ContactPhone);
        user.ChangeRole(request.RoleId);
        user.SetStatus(request.Status.ToDomain());

        users.Update(user);

        await eventBus.PublishAsync(
            new UserUpdatedIntegrationEvent(user.Id, user.Email, user.Username, user.FullName),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
