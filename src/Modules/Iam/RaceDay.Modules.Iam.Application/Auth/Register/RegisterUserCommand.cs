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
    string Username,
    string Password,
    string FullName,
    string? Phone,
    DateOnly? DateOfBirth,
    GenderDto Gender) : IRequest<RegisterUserResult>;

public sealed record RegisterUserResult(Guid UserId, string Username, string Message);

public sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
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
        var existingEmail = await users.GetByEmailAsync(request.Email, cancellationToken);
        if (existingEmail is not null)
        {
            // Verified (or locked) → block. Unverified Pending → replace info + gửi OTP mới.
            if (existingEmail.EmailVerified || existingEmail.Status == UserStatus.Locked)
            {
                throw new DomainException("Email đã được đăng ký.");
            }

            await EnsureUsernameAvailableAsync(request.Username, existingEmail.Id, cancellationToken);

            existingEmail.ReplaceUnverifiedRegistration(
                request.Username,
                passwordHasher.Hash(request.Password),
                request.FullName,
                request.Phone,
                request.DateOfBirth,
                request.Gender.ToDomain());

            users.Update(existingEmail);
            await eventBus.PublishAsync(
                new UserCreatedIntegrationEvent(
                    existingEmail.Id,
                    existingEmail.Email,
                    existingEmail.Username,
                    existingEmail.FullName),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            await otpService.IssueAndSendAsync(
                existingEmail.Email,
                existingEmail.Username,
                existingEmail.Id,
                OtpPurpose.EmailVerification,
                cancellationToken);

            return new RegisterUserResult(
                existingEmail.Id,
                existingEmail.Username,
                "Đăng ký thành công. Vui lòng kiểm tra email và nhập OTP để xác thực tài khoản.");
        }

        await EnsureUsernameAvailableAsync(request.Username, excludeUserId: null, cancellationToken);

        var role = await roles.GetByNameAsync("User", cancellationToken)
            ?? throw new DomainException("Role User chưa được cấu hình.");

        var user = User.Create(
            request.Email,
            request.Username,
            passwordHasher.Hash(request.Password),
            request.FullName,
            role.Id,
            request.Phone,
            request.DateOfBirth,
            request.Gender.ToDomain(),
            UserStatus.Pending);

        await users.AddAsync(user, cancellationToken);
        await eventBus.PublishAsync(
            new UserCreatedIntegrationEvent(user.Id, user.Email, user.Username, user.FullName),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await otpService.IssueAndSendAsync(
            user.Email,
            user.Username,
            user.Id,
            OtpPurpose.EmailVerification,
            cancellationToken);

        return new RegisterUserResult(
            user.Id,
            user.Username,
            "Đăng ký thành công. Vui lòng kiểm tra email và nhập OTP để xác thực tài khoản.");
    }

    private async Task EnsureUsernameAvailableAsync(
        string username,
        Guid? excludeUserId,
        CancellationToken cancellationToken)
    {
        var existingUsername = await users.GetByUsernameAsync(username, cancellationToken);
        if (existingUsername is not null && existingUsername.Id != excludeUserId)
        {
            throw new DomainException("Tên đăng nhập đã được sử dụng.");
        }
    }
}
