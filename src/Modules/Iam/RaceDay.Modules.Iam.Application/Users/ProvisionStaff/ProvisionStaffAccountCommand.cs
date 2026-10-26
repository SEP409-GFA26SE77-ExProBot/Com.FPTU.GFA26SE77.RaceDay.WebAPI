using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.Application.Users.ProvisionStaff;

/// <summary>
/// Cấp account staff tách biệt Runner: IAM email/username được gen (đã verify sẵn, không OTP).
/// <paramref name="NotifyEmail"/> chỉ để SMTP gửi username + mật khẩu mặc định.
/// </summary>
public sealed record ProvisionStaffAccountCommand(
    string NotifyEmail,
    string FullName,
    string StaffRoleName,
    string? Phone,
    string? EventName,
    string? StationLabel) : IRequest<ProvisionStaffAccountResult>;

public sealed record ProvisionStaffAccountResult(
    Guid UserId,
    string Username,
    string AccountEmail,
    bool CreatedNew,
    bool PasswordSentByEmail);

public sealed class ProvisionStaffAccountCommandValidator : AbstractValidator<ProvisionStaffAccountCommand>
{
    public ProvisionStaffAccountCommandValidator()
    {
        RuleFor(x => x.NotifyEmail).NotEmpty().EmailAddress().MaximumLength(100);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.StaffRoleName).NotEmpty();
        RuleFor(x => x.Phone).MaximumLength(20);
    }
}

public sealed class ProvisionStaffAccountCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    IEventBus eventBus,
    IEmailSender emailSender,
    IConfiguration configuration) : IRequestHandler<ProvisionStaffAccountCommand, ProvisionStaffAccountResult>
{
    private static readonly HashSet<string> AllowedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Coordinator", "Photographer", "Volunteer"
    };

    public async Task<ProvisionStaffAccountResult> Handle(
        ProvisionStaffAccountCommand request,
        CancellationToken cancellationToken)
    {
        var roleName = request.StaffRoleName.Trim();
        if (!AllowedRoles.Contains(roleName))
        {
            throw new DomainException("Role nhân sự không hợp lệ.");
        }

        var role = await roles.GetByNameAsync(roleName, cancellationToken)
            ?? throw new DomainException($"Role {roleName} chưa được seed.");

        var notifyEmail = request.NotifyEmail.Trim().ToLowerInvariant();
        var rolePrefix = roleName.ToLowerInvariant(); // photographer | coordinator | volunteer
        var username = await AllocateUsernameAsync(rolePrefix, cancellationToken);
        // Email IAM gen sẵn — không dùng Gmail cá nhân (tránh đụng account Runner)
        var accountEmail = await AllocateAccountEmailAsync(rolePrefix, username, cancellationToken);

        var plainPassword = GenerateTempPassword();
        var user = User.Create(
            accountEmail,
            username,
            passwordHasher.Hash(plainPassword),
            request.FullName.Trim(),
            role.Id,
            string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            dateOfBirth: null,
            Gender.Unknown,
            UserStatus.Active);
        // Staff invite ≠ đăng ký public: bỏ OTP, coi email đã verify
        user.ConfirmEmailVerification();
        user.RequirePasswordChange();

        await users.AddAsync(user, cancellationToken);
        await eventBus.PublishAsync(
            new UserCreatedIntegrationEvent(user.Id, user.Email, user.Username, user.FullName),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var loginUrl = configuration["Frontend:AdminAppUrl"]?.TrimEnd('/')
            ?? configuration["Frontend:UserAppUrl"]?.TrimEnd('/')
            ?? "http://localhost:3001";

        var eventLine = string.IsNullOrWhiteSpace(request.EventName)
            ? ""
            : $"<p>Giải: <strong>{Escape(request.EventName)}</strong></p>";
        var stationLine = string.IsNullOrWhiteSpace(request.StationLabel)
            ? ""
            : $"<p>Nhiệm vụ / điểm: <strong>{Escape(request.StationLabel)}</strong></p>";

        await emailSender.SendAsync(
            new EmailMessage(
                notifyEmail,
                $"[RaceDay] Tài khoản {roleName} — kích hoạt nhân sự",
                $"""
                <p>Xin chào <strong>{Escape(request.FullName)}</strong>,</p>
                <p>Bạn được cấp tài khoản <strong>{Escape(roleName)}</strong> trên RaceDay
                (không cần đăng ký / không nhập OTP).</p>
                {eventLine}{stationLine}
                <p>Đăng nhập tại: <a href="{Escape(loginUrl)}">{Escape(loginUrl)}</a></p>
                <ul>
                  <li>Tên đăng nhập: <code>{Escape(username)}</code></li>
                  <li>Mật khẩu mặc định: <code>{Escape(plainPassword)}</code></li>
                </ul>
                <p class="muted">Email hệ thống của tài khoản: <code>{Escape(accountEmail)}</code>
                (chỉ dùng nội bộ — đăng nhập bằng username ở trên).</p>
                <p>Sau khi đăng nhập thành công, hãy đổi mật khẩu mặc định.</p>
                """,
                $"Username: {username}\nPassword: {plainPassword}\nLogin: {loginUrl}\nAccountEmail: {accountEmail}"),
            cancellationToken);

        return new ProvisionStaffAccountResult(
            user.Id,
            username,
            accountEmail,
            CreatedNew: true,
            PasswordSentByEmail: true);
    }

    private async Task<string> AllocateUsernameAsync(string rolePrefix, CancellationToken ct)
    {
        var baseName = Regex.Replace($"{rolePrefix}Account", @"[^a-zA-Z0-9._-]", "");
        if (baseName.Length > 32)
        {
            baseName = baseName[..32];
        }

        for (var i = 0; i < 40; i++)
        {
            var suffix = RandomNumberGenerator.GetInt32(1000, 9999);
            var candidate = i == 0 ? $"{baseName}{suffix}" : $"{baseName}{suffix}{i}";
            if (candidate.Length > User.UsernameMaxLength)
            {
                candidate = candidate[..User.UsernameMaxLength];
            }

            if (await users.GetByUsernameAsync(candidate, ct) is null)
            {
                return candidate;
            }
        }

        return $"staff{Guid.NewGuid():N}"[..16];
    }

    private async Task<string> AllocateAccountEmailAsync(
        string rolePrefix,
        string username,
        CancellationToken ct)
    {
        // Domain nội bộ — không xung đột Gmail Runner thật
        for (var i = 0; i < 20; i++)
        {
            var local = i == 0
                ? username.ToLowerInvariant()
                : $"{rolePrefix}.{Guid.NewGuid():N}"[..18];
            var email = $"{local}@staff.raceday.local";
            if (await users.GetByEmailAsync(email, ct) is null)
            {
                return email;
            }
        }

        return $"{Guid.NewGuid():N}@staff.raceday.local";
    }

    private static string GenerateTempPassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var bytes = new byte[10];
        RandomNumberGenerator.Fill(bytes);
        var sb = new StringBuilder("Rd@");
        foreach (var b in bytes)
        {
            sb.Append(alphabet[b % alphabet.Length]);
        }

        return sb.ToString();
    }

    private static string Escape(string value) =>
        System.Net.WebUtility.HtmlEncode(value);
}
