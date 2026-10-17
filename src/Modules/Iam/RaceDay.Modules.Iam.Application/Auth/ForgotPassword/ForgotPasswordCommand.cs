using FluentValidation;
using MediatR;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Domain.Users;

namespace RaceDay.Modules.Iam.Application.Auth.ForgotPassword;

public sealed record ForgotPasswordCommand(string Email) : IRequest;

public sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
        => RuleFor(x => x.Email).NotEmpty().EmailAddress();
}

/// <summary>
/// Always returns success to avoid email enumeration. Sends OTP only when the account exists.
/// </summary>
public sealed class ForgotPasswordCommandHandler(
    IUserRepository users,
    IEmailOtpService otpService) : IRequestHandler<ForgotPasswordCommand>
{
    public async Task Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByEmailAsync(request.Email, cancellationToken);
        if (user is null || user.Status == UserStatus.Locked)
        {
            return;
        }

        await otpService.IssueAndSendAsync(
            user.Email,
            user.Username,
            user.Id,
            OtpPurpose.PasswordReset,
            cancellationToken);
    }
}
