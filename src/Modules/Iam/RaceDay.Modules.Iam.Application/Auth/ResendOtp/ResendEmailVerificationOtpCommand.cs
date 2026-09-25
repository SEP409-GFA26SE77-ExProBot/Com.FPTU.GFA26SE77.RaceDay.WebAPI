using FluentValidation;
using MediatR;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.Application.Auth.ResendOtp;

public sealed record ResendEmailVerificationOtpCommand(string Email) : IRequest;

public sealed class ResendEmailVerificationOtpCommandValidator : AbstractValidator<ResendEmailVerificationOtpCommand>
{
    public ResendEmailVerificationOtpCommandValidator()
        => RuleFor(x => x.Email).NotEmpty().EmailAddress();
}

public sealed class ResendEmailVerificationOtpCommandHandler(
    IUserRepository users,
    IEmailOtpService otpService) : IRequestHandler<ResendEmailVerificationOtpCommand>
{
    public async Task Handle(ResendEmailVerificationOtpCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByEmailAsync(request.Email, cancellationToken)
            ?? throw new DomainException("Không tìm thấy tài khoản.");

        if (user.EmailVerified && user.Status == UserStatus.Active)
        {
            throw new DomainException("Email đã được xác thực.");
        }

        await otpService.IssueAndSendAsync(
            user.Email,
            user.Id,
            OtpPurpose.EmailVerification,
            cancellationToken);
    }
}
