using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.Domain.Users;

public sealed class EmailOtp : Entity
{
    public const int DefaultTtlMinutes = 10;
    public const int MaxAttempts = 5;

    public Guid? UserId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public OtpPurpose Purpose { get; private set; }
    public string CodeHash { get; private set; } = string.Empty;
    public DateTime ExpiresOnUtc { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime? ConsumedOnUtc { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    private EmailOtp()
    {
    }

    public static EmailOtp Issue(
        string email,
        OtpPurpose purpose,
        string codeHash,
        Guid? userId = null,
        TimeSpan? ttl = null)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainException("Email không được để trống.");
        }

        if (string.IsNullOrWhiteSpace(codeHash))
        {
            throw new DomainException("OTP hash không hợp lệ.");
        }

        var now = DateTime.UtcNow;
        return new EmailOtp
        {
            UserId = userId,
            Email = email.Trim().ToLowerInvariant(),
            Purpose = purpose,
            CodeHash = codeHash,
            ExpiresOnUtc = now.Add(ttl ?? TimeSpan.FromMinutes(DefaultTtlMinutes)),
            AttemptCount = 0,
            CreatedOnUtc = now
        };
    }

    public bool IsExpired(DateTime utcNow) => utcNow >= ExpiresOnUtc;

    public bool IsConsumed => ConsumedOnUtc is not null;

    public void RegisterFailedAttempt()
    {
        AttemptCount++;
        if (AttemptCount >= MaxAttempts)
        {
            ConsumedOnUtc = DateTime.UtcNow;
        }
    }

    public void EnsureCanVerify(DateTime utcNow)
    {
        if (IsConsumed)
        {
            throw new DomainException("OTP đã được sử dụng hoặc bị vô hiệu.");
        }

        if (IsExpired(utcNow))
        {
            throw new DomainException("OTP đã hết hạn.");
        }

        if (AttemptCount >= MaxAttempts)
        {
            throw new DomainException("OTP đã vượt quá số lần thử.");
        }
    }

    public void Consume()
    {
        ConsumedOnUtc = DateTime.UtcNow;
    }
}
