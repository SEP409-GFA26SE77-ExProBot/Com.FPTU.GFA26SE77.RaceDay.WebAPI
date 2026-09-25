using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;
using RaceDay.Modules.Iam.Domain.Profiles;

namespace RaceDay.Modules.Iam.Domain.Users;

public sealed class User : AggregateRoot
{
    public string Email { get; private set; } = string.Empty;
    public Guid RoleId { get; private set; }
    public string PasswordHash { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public DateOnly? DateOfBirth { get; private set; }
    public Gender Gender { get; private set; } = Gender.Unknown;
    public UserStatus Status { get; private set; } = UserStatus.Pending;
    public bool EmailVerified { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public Profile? Profile { get; private set; }

    private User()
    {
    }

    public static User Create(
        string email,
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
        user.Raise(new UserCreatedDomainEvent(user.Id, user.Email, user.FullName));
        return user;
    }

    public void UpdateProfile(
        string fullName,
        string? phone,
        DateOnly? dateOfBirth,
        Gender gender,
        string? profileDescription,
        string? contactEmail,
        string? contactPhone)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new DomainException("Họ tên không được để trống.");
        }

        FullName = fullName.Trim();
        Phone = phone;
        DateOfBirth = dateOfBirth;
        Gender = gender;
        UpdatedAt = DateTime.UtcNow;
        Profile?.UpdateContact(profileDescription, contactEmail, contactPhone);
        Raise(new UserUpdatedDomainEvent(Id, Email, FullName));
    }

    public void ChangeRole(Guid roleId)
    {
        if (roleId == Guid.Empty)
        {
            throw new DomainException("RoleId không hợp lệ.");
        }

        RoleId = roleId;
        UpdatedAt = DateTime.UtcNow;
        Raise(new UserUpdatedDomainEvent(Id, Email, FullName));
    }

    public void SetStatus(UserStatus status)
    {
        Status = status;
        UpdatedAt = DateTime.UtcNow;
        Raise(new UserUpdatedDomainEvent(Id, Email, FullName));
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
        UpdatedAt = DateTime.UtcNow;
        Raise(new PasswordChangedDomainEvent(Id, Email));
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
}

public sealed record UserCreatedDomainEvent(Guid UserId, string Email, string FullName) : DomainEvent;
public sealed record UserUpdatedDomainEvent(Guid UserId, string Email, string FullName) : DomainEvent;
public sealed record UserLockedDomainEvent(Guid UserId, string Email) : DomainEvent;
public sealed record UserUnlockedDomainEvent(Guid UserId, string Email) : DomainEvent;
public sealed record UserEmailVerifiedDomainEvent(Guid UserId, string Email) : DomainEvent;
public sealed record PasswordChangedDomainEvent(Guid UserId, string Email) : DomainEvent;
