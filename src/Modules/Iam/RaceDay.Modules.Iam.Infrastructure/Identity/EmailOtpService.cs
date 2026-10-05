using System.Security.Cryptography;
using System.Text;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Modules.Iam.Infrastructure.Email;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.Infrastructure.Identity;

public sealed class EmailOtpService(
    IEmailOtpRepository otps,
    IEmailSender emailSender,
    IUnitOfWork unitOfWork) : IEmailOtpService
{
    public async Task<string> IssueAndSendAsync(
        string email,
        string username,
        Guid? userId,
        OtpPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var displayUsername = string.IsNullOrWhiteSpace(username)
            ? normalized
            : username.Trim().ToLowerInvariant();
        await otps.InvalidateActiveAsync(normalized, purpose, cancellationToken);

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var otp = EmailOtp.Issue(normalized, purpose, Hash(normalized, purpose, code), userId);
        await otps.AddAsync(otp, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var message = RaceDayOtpEmailTemplates.Build(normalized, displayUsername, purpose, code);
        await emailSender.SendAsync(message, cancellationToken);

        return code;
    }

    public async Task<EmailOtp> VerifyAsync(
        string email,
        OtpPurpose purpose,
        string code,
        CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var otp = await otps.GetLatestActiveAsync(normalized, purpose, cancellationToken)
            ?? throw new DomainException("OTP không hợp lệ hoặc đã hết hạn.");

        var now = DateTime.UtcNow;
        otp.EnsureCanVerify(now);

        if (!FixedTimeEquals(otp.CodeHash, Hash(normalized, purpose, code.Trim())))
        {
            otp.RegisterFailedAttempt();
            otps.Update(otp);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new DomainException("OTP không đúng.");
        }

        otp.Consume();
        otps.Update(otp);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return otp;
    }

    private static string Hash(string email, OtpPurpose purpose, string code)
    {
        var raw = $"{email}|{(short)purpose}|{code}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
