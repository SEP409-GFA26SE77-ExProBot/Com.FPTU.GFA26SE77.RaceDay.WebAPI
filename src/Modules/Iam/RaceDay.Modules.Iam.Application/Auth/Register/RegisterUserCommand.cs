using FluentValidation;
using MediatR;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Application.Users;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.Application.Auth.Register;

public sealed record RegisterUserCommand(
    string Email,
    string Password,
    string FullName,
    string? Phone,
    DateOnly? DateOfBirth,
    GenderDto Gender) : IRequest<RegisterUserResult>;

public sealed record RegisterUserResult(Guid UserId, string Message);

public sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(100);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(100);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Phone).MaximumLength(20).When(x => x.Phone is not null);
        RuleFor(x => x.Gender).IsInEnum();
    }
}

public sealed class RegisterUserCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    IPasswordHasher passwordHasher,
    IEmailOtpService otpService,
    IUnitOfWork unitOfWork,
    IEventBus eventBus) : IRequestHandler<RegisterUserCommand, RegisterUserResult>
{
    public async Task<RegisterUserResult> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
    {
        var existing = await users.GetByEmailAsync(request.Email, cancellationToken);
        if (existing is not null)
        {
            throw new DomainException("Email đã được đăng ký.");
        }

        var role = await roles.GetByNameAsync("User", cancellationToken)
            ?? throw new DomainException("Role User chưa được cấu hình.");

        var user = User.Create(
            request.Email,
            passwordHasher.Hash(request.Password),
            request.FullName,
            role.Id,
            request.Phone,
            request.DateOfBirth,
            request.Gender.ToDomain(),
            UserStatus.Pending);

        await users.AddAsync(user, cancellationToken);
        await eventBus.PublishAsync(
            new UserCreatedIntegrationEvent(user.Id, user.Email, user.FullName),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await otpService.IssueAndSendAsync(
            user.Email,
            user.Id,
            OtpPurpose.EmailVerification,
            cancellationToken);

        return new RegisterUserResult(
            user.Id,
            "Đăng ký thành công. Vui lòng kiểm tra email và nhập OTP để xác thực tài khoản.");
    }
}
