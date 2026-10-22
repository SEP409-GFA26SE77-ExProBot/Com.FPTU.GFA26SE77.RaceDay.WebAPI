using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;
using RaceDay.Modules.Iam.Domain.Profiles;

namespace RaceDay.Modules.Iam.Domain.Users;

public sealed class User : AggregateRoot
{
    public const int UsernameMinLength = 3;
    public const int UsernameMaxLength = 50;

    public string Email { get; private set; } = string.Empty;
    public string Username { get; private set; } = string.Empty;
    public Guid RoleId { get; private set; }
    public string PasswordHash { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public DateOnly? DateOfBirth { get; private set; }
    /// <summary>CCCD/CMND (9|12 số) hoặc hộ chiếu.</summary>
    public string? IdNumber { get; private set; }
    public Gender Gender { get; private set; } = Gender.Unknown;
    public UserStatus Status { get; private set; } = UserStatus.Pending;
    public bool EmailVerified { get; private set; }
    /// <summary>Tài khoản cấp bởi BTC/admin — nhắc đổi mật khẩu sau lần đăng nhập đầu.</summary>
    public bool MustChangePassword { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public Profile? Profile { get; private set; }

    private User()
    {
    }

    public static User Create(
        string email,
        string username,
        string passwordHash,
        string fullName,
        Guid roleId,
        string? phone = null,
        DateOnly? dateOfBirth = null,
        Gender gender = Gender.Unknown,
        UserStatus status = UserStatus.Pending)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainException("Email không được để trống.");
        }

        var normalizedUsername = NormalizeUsername(username);

        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new DomainException("Họ tên không được để trống.");
        }

        if (roleId == Guid.Empty)
        {
            throw new DomainException("RoleId không hợp lệ.");
        }

        var now = DateTime.UtcNow;
        var user = new User
        {
            Email = email.Trim().ToLowerInvariant(),
            Username = normalizedUsername,
            PasswordHash = passwordHash,
            FullName = fullName.Trim(),
            RoleId = roleId,
            Phone = phone,
            DateOfBirth = dateOfBirth,
            Gender = gender,
            Status = status,
            EmailVerified = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        user.Profile = Profile.CreateForUser(user.Id, description: $"Hồ sơ của {user.FullName}", contactEmail: user.Email, contactPhone: phone);
        user.Raise(new UserCreatedDomainEvent(user.Id, user.Email, user.Username, user.FullName));
        return user;
    }

    public void ChangeUsername(string username)
    {
        Username = NormalizeUsername(username);
        UpdatedAt = DateTime.UtcNow;
        Raise(new UserUpdatedDomainEvent(Id, Email, Username, FullName));
    }

    /// <summary>
    /// Re-register before email OTP verify: replace username/password/profile; keep same user id + email.
    /// </summary>
    public void ReplaceUnverifiedRegistration(
        string username,
        string passwordHash,
        string fullName,
        string? phone,
        DateOnly? dateOfBirth,
        Gender gender)
    {
        if (EmailVerified)
        {
            throw new DomainException("Email đã được đăng ký.");
        }

        if (Status == UserStatus.Locked)
        {
            throw new DomainException("Tài khoản đã bị khóa.");
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new DomainException("Họ tên không được để trống.");
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("Password hash không hợp lệ.");
        }

        Username = NormalizeUsername(username);
        PasswordHash = passwordHash;
        FullName = fullName.Trim();
        Phone = phone;
        DateOfBirth = dateOfBirth;
        Gender = gender;
        Status = UserStatus.Pending;
        EmailVerified = false;
        UpdatedAt = DateTime.UtcNow;
        Profile?.UpdateContact($"Hồ sơ của {FullName}", Email, phone);
        Raise(new UserUpdatedDomainEvent(Id, Email, Username, FullName));
    }

    public void UpdateProfile(
        string fullName,
        string? phone,
        DateOnly? dateOfBirth,
        Gender gender,
        string? profileDescription,
        string? contactEmail,
        string? contactPhone,
        string? idNumber = null)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new DomainException("Họ tên không được để trống.");
        }

        FullName = fullName.Trim();
        Phone = phone;
        DateOfBirth = dateOfBirth;
        Gender = gender;
        IdNumber = string.IsNullOrWhiteSpace(idNumber) ? null : idNumber.Trim().ToUpperInvariant();
        UpdatedAt = DateTime.UtcNow;
        Profile?.UpdateContact(profileDescription, contactEmail, contactPhone);
        Raise(new UserUpdatedDomainEvent(Id, Email, Username, FullName));
    }

    public void SetAvatarUrl(string? avatarUrl)
    {
        if (Profile is null)
        {
            throw new DomainException("Hồ sơ chưa được khởi tạo.");
        }

        Profile.SetAvatarUrl(avatarUrl);
        UpdatedAt = DateTime.UtcNow;
        Raise(new UserUpdatedDomainEvent(Id, Email, Username, FullName));
    }

    public void ChangeRole(Guid roleId)
    {
        if (roleId == Guid.Empty)
        {
            throw new DomainException("RoleId không hợp lệ.");
        }

        RoleId = roleId;
        UpdatedAt = DateTime.UtcNow;
        Raise(new UserUpdatedDomainEvent(Id, Email, Username, FullName));
    }

    public void SetStatus(UserStatus status)
    {
        Status = status;
        UpdatedAt = DateTime.UtcNow;
        Raise(new UserUpdatedDomainEvent(Id, Email, Username, FullName));
    }

    public void Lock()
    {
        if (Status == UserStatus.Locked)
        {
            return;
        }

        Status = UserStatus.Locked;
        UpdatedAt = DateTime.UtcNow;
        Raise(new UserLockedDomainEvent(Id, Email));
    }

    public void Unlock()
    {
        if (Status != UserStatus.Locked)
        {
            return;
        }

        Status = EmailVerified ? UserStatus.Active : UserStatus.Pending;
        UpdatedAt = DateTime.UtcNow;
        Raise(new UserUnlockedDomainEvent(Id, Email));
    }

    public void SetPasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("Password hash không hợp lệ.");
        }

        PasswordHash = passwordHash;
        MustChangePassword = false;
        UpdatedAt = DateTime.UtcNow;
        Raise(new PasswordChangedDomainEvent(Id, Email));
    }

    public void RequirePasswordChange()
    {
        MustChangePassword = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ClearMustChangePassword()
    {
        if (!MustChangePassword)
        {
            return;
        }

        MustChangePassword = false;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Marks email verified and activates account when it was Pending.</summary>
    public void ConfirmEmailVerification()
    {
        EmailVerified = true;
        if (Status == UserStatus.Pending)
        {
            Status = UserStatus.Active;
        }

        UpdatedAt = DateTime.UtcNow;
        Raise(new UserEmailVerifiedDomainEvent(Id, Email));
    }

    /// <summary>Admin-provisioned users that should skip OTP.</summary>
    public void MarkEmailVerified()
    {
        EmailVerified = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public void EnsureCanLogin()
    {
        if (Status == UserStatus.Locked)
        {
            throw new DomainException("Tài khoản đã bị khóa.");
        }

        if (Status == UserStatus.Pending || !EmailVerified)
        {
            throw new DomainException("Tài khoản chưa xác thực email. Vui lòng nhập OTP.");
        }
    }

    public static string NormalizeUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new DomainException("Tên đăng nhập không được để trống.");
        }

        var normalized = username.Trim().ToLowerInvariant();
        if (normalized.Length < UsernameMinLength || normalized.Length > UsernameMaxLength)
        {
            throw new DomainException($"Tên đăng nhập phải từ {UsernameMinLength}–{UsernameMaxLength} ký tự.");
        }

        foreach (var ch in normalized)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '_' or '.' or '-')
            {
                continue;
            }

            throw new DomainException("Tên đăng nhập chỉ gồm chữ, số, dấu chấm, gạch dưới hoặc gạch ngang.");
        }

        return normalized;
    }
}

public sealed record UserCreatedDomainEvent(Guid UserId, string Email, string Username, string FullName) : DomainEvent;
public sealed record UserUpdatedDomainEvent(Guid UserId, string Email, string Username, string FullName) : DomainEvent;
public sealed record UserLockedDomainEvent(Guid UserId, string Email) : DomainEvent;
public sealed record UserUnlockedDomainEvent(Guid UserId, string Email) : DomainEvent;
public sealed record UserEmailVerifiedDomainEvent(Guid UserId, string Email) : DomainEvent;
public sealed record PasswordChangedDomainEvent(Guid UserId, string Email) : DomainEvent;
