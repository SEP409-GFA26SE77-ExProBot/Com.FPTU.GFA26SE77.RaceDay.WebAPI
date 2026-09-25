using FluentValidation;
using MediatR;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.Application.Auth.VerifyEmail;

public sealed record VerifyEmailCommand(string Email, string Otp) : IRequest;

public sealed class VerifyEmailCommandValidator : AbstractValidator<VerifyEmailCommand>
{
    public VerifyEmailCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Otp).NotEmpty().Length(6);
    }
}

public sealed class VerifyEmailCommandHandler(
    IUserRepository users,
    IEmailOtpService otpService,
    IUnitOfWork unitOfWork,
    IEventBus eventBus) : IRequestHandler<VerifyEmailCommand>
{
    public async Task Handle(VerifyEmailCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByEmailAsync(request.Email, cancellationToken)
            ?? throw new DomainException("Không tìm thấy tài khoản.");

        if (user.EmailVerified && user.Status == UserStatus.Active)
        {
            return;
        }

        await otpService.VerifyAsync(
            user.Email,
            OtpPurpose.EmailVerification,
            request.Otp,
            cancellationToken);

        user.ConfirmEmailVerification();
        users.Update(user);

        await eventBus.PublishAsync(
            new UserEmailVerifiedIntegrationEvent(user.Id, user.Email),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
