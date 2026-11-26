using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.Application.Users.ProvisionStaff;

/// <summary>Khôi phục mật khẩu gốc staff; gửi lại email liên hệ nếu có.</summary>
public sealed record ResetStaffCredentialsCommand(
    Guid UserId,
    string? NotifyEmail,
    string FullName,
    string? EventName) : IRequest<ResetStaffCredentialsResult>;

public sealed record ResetStaffCredentialsResult(
    string Username,
    string PlainPassword,
    bool PasswordSentByEmail);

public sealed class ResetStaffCredentialsCommandValidator : AbstractValidator<ResetStaffCredentialsCommand>
{
    public ResetStaffCredentialsCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.NotifyEmail)
            .EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.NotifyEmail));
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
    }
}

public sealed class ResetStaffCredentialsCommandHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    IEventBus eventBus,
    IEmailSender emailSender,
    IConfiguration configuration) : IRequestHandler<ResetStaffCredentialsCommand, ResetStaffCredentialsResult>
{
    public async Task<ResetStaffCredentialsResult> Handle(
        ResetStaffCredentialsCommand request,
        CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy tài khoản nhân sự.");

        if (user.Status == UserStatus.Locked)
        {
            throw new DomainException("Tài khoản đang bị khóa — mở khóa trước khi khôi phục mật khẩu.");
        }

        var plainPassword = StaffCredentialDefaults.ResolveDefaultPassword(configuration);
        user.SetPasswordHash(passwordHasher.Hash(plainPassword));
        user.RequirePasswordChange();
        users.Update(user);

        await eventBus.PublishAsync(new PasswordResetIntegrationEvent(user.Id, user.Email), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var notify = string.IsNullOrWhiteSpace(request.NotifyEmail)
            ? null
            : request.NotifyEmail.Trim().ToLowerInvariant();
        var passwordSent = false;
        if (notify is not null && !notify.EndsWith("@staff.raceday.local", StringComparison.OrdinalIgnoreCase))
        {
            var loginUrl = configuration["Frontend:AdminAppUrl"]?.TrimEnd('/')
                ?? configuration["Frontend:UserAppUrl"]?.TrimEnd('/')
                ?? "http://localhost:3001";
            var eventLine = string.IsNullOrWhiteSpace(request.EventName)
                ? ""
                : $"<p>Giải: <strong>{Escape(request.EventName)}</strong></p>";

            await emailSender.SendAsync(
                new EmailMessage(
                    notify,
                    "[RaceDay] Mật khẩu nhân sự đã được khôi phục",
                    $"""
                    <p>Xin chào <strong>{Escape(request.FullName)}</strong>,</p>
                    <p>Ban tổ chức đã khôi phục mật khẩu gốc tài khoản nhân sự của bạn.</p>
                    {eventLine}
                    <p>Đăng nhập tại: <a href="{Escape(loginUrl)}">{Escape(loginUrl)}</a></p>
                    <ul>
                      <li>Tên đăng nhập: <code>{Escape(user.Username)}</code></li>
                      <li>Mật khẩu gốc: <code>{Escape(plainPassword)}</code></li>
                    </ul>
                    <p>Hãy đổi mật khẩu sau khi đăng nhập.</p>
                    """,
                    $"Username: {user.Username}\nPassword: {plainPassword}\nLogin: {loginUrl}"),
                cancellationToken);
            passwordSent = true;
        }

        return new ResetStaffCredentialsResult(user.Username, plainPassword, passwordSent);
    }

    private static string Escape(string value) => System.Net.WebUtility.HtmlEncode(value);
}
