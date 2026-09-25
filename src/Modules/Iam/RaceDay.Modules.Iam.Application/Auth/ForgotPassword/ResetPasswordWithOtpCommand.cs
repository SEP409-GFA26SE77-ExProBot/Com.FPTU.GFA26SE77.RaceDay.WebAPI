using FluentValidation;
using MediatR;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.Application.Auth.ForgotPassword;

public sealed record ResetPasswordWithOtpCommand(string Email, string Otp, string NewPassword) : IRequest;

public sealed class ResetPasswordWithOtpCommandValidator : AbstractValidator<ResetPasswordWithOtpCommand>
{
    public ResetPasswordWithOtpCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Otp).NotEmpty().Length(6);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(100);
    }
}

public sealed class ResetPasswordWithOtpCommandHandler(
    IUserRepository users,
    IEmailOtpService otpService,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    IEventBus eventBus) : IRequestHandler<ResetPasswordWithOtpCommand>
{
    public async Task Handle(ResetPasswordWithOtpCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByEmailAsync(request.Email, cancellationToken)
            ?? throw new DomainException("OTP không hợp lệ hoặc đã hết hạn.");

        await otpService.VerifyAsync(
            user.Email,
            OtpPurpose.PasswordReset,
            request.Otp,
            cancellationToken);

        user.SetPasswordHash(passwordHasher.Hash(request.NewPassword));
        users.Update(user);

        await eventBus.PublishAsync(new PasswordResetIntegrationEvent(user.Id, user.Email), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
