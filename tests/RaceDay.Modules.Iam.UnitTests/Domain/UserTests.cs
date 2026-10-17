using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.UnitTests.Domain;

public class UserTests
{
    private static readonly Guid RoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Create_Defaults_To_Pending_Unverified()
    {
        var user = User.Create("An.Nguyen@RaceDay.vn", "an.nguyen", "hash:demo", "  Nguyễn Văn An  ", RoleId);

        Assert.Equal("an.nguyen@raceday.vn", user.Email);
        Assert.Equal("an.nguyen", user.Username);
        Assert.Equal("Nguyễn Văn An", user.FullName);
        Assert.Equal(UserStatus.Pending, user.Status);
        Assert.False(user.EmailVerified);
        Assert.NotNull(user.Profile);
        Assert.Contains(user.DomainEvents, e => e is UserCreatedDomainEvent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyEmail_Throws(string? email)
    {
        var ex = Assert.Throws<DomainException>(() =>
            User.Create(email!, "uname", "hash", "Name", RoleId));
        Assert.Contains("Email", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyUsername_Throws(string? username)
    {
        var ex = Assert.Throws<DomainException>(() =>
            User.Create("a@b.com", username!, "hash", "Name", RoleId));
        Assert.Contains("Tên đăng nhập", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyFullName_Throws(string? fullName)
    {
        var ex = Assert.Throws<DomainException>(() =>
            User.Create("a@b.com", "uname", "hash", fullName!, RoleId));
        Assert.Contains("Họ tên", ex.Message);
    }

    [Fact]
    public void Create_EmptyRoleId_Throws()
    {
        var ex = Assert.Throws<DomainException>(() =>
            User.Create("a@b.com", "uname", "hash", "Name", Guid.Empty));
        Assert.Contains("RoleId", ex.Message);
    }

    [Fact]
    public void UpdateProfile_EmptyFullName_Throws()
    {
        var user = User.Create("a@b.com", "uname", "hash", "Name", RoleId);
        user.ClearDomainEvents();

        var ex = Assert.Throws<DomainException>(() =>
            user.UpdateProfile("  ", null, null, Gender.Male, null, null, null));
        Assert.Contains("Họ tên", ex.Message);
    }

    [Fact]
    public void ChangeRole_Empty_Throws()
    {
        var user = User.Create("a@b.com", "uname", "hash", "Name", RoleId);
        Assert.Throws<DomainException>(() => user.ChangeRole(Guid.Empty));
    }

    [Fact]
    public void ChangeRole_Updates_RoleId()
    {
        var user = User.Create("a@b.com", "uname", "hash", "Name", RoleId);
        var newRole = Guid.Parse("11111111-1111-1111-1111-111111111111");
        user.ClearDomainEvents();

        user.ChangeRole(newRole);

        Assert.Equal(newRole, user.RoleId);
        Assert.Contains(user.DomainEvents, e => e is UserUpdatedDomainEvent);
    }

    [Fact]
    public void SetPasswordHash_Empty_Throws()
    {
        var user = User.Create("a@b.com", "uname", "hash", "Name", RoleId);
        Assert.Throws<DomainException>(() => user.SetPasswordHash(" "));
    }

    [Fact]
    public void EnsureCanLogin_Pending_Throws()
    {
        var user = User.Create("p@raceday.vn", "pending", "hash", "Pending", RoleId);
        var ex = Assert.Throws<DomainException>(() => user.EnsureCanLogin());
        Assert.Contains("chưa xác thực", ex.Message);
    }

    [Fact]
    public void ConfirmEmailVerification_Activates_Account()
    {
        var user = User.Create("v@raceday.vn", "verify", "hash", "Verify", RoleId);
        user.ClearDomainEvents();

        user.ConfirmEmailVerification();

        Assert.True(user.EmailVerified);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Contains(user.DomainEvents, e => e is UserEmailVerifiedDomainEvent);
        user.EnsureCanLogin();
    }

    [Fact]
    public void ReplaceUnverifiedRegistration_Updates_Pending_User()
    {
        var user = User.Create("p@raceday.vn", "olduser", "old-hash", "Old", RoleId);
        user.ClearDomainEvents();

        user.ReplaceUnverifiedRegistration(
            "newuser", "new-hash", "New Name", "0901234567", new DateOnly(1999, 1, 1), Gender.Female);

        Assert.Equal("newuser", user.Username);
        Assert.Equal("new-hash", user.PasswordHash);
        Assert.Equal("New Name", user.FullName);
        Assert.Equal("0901234567", user.Phone);
        Assert.Equal(new DateOnly(1999, 1, 1), user.DateOfBirth);
        Assert.Equal(Gender.Female, user.Gender);
        Assert.Equal(UserStatus.Pending, user.Status);
        Assert.False(user.EmailVerified);
        Assert.Contains(user.DomainEvents, e => e is UserUpdatedDomainEvent);
    }

    [Fact]
    public void ReplaceUnverifiedRegistration_Verified_Throws()
    {
        var user = User.Create("v@raceday.vn", "vuser", "hash", "V", RoleId);
        user.ConfirmEmailVerification();

        Assert.Throws<DomainException>(() =>
            user.ReplaceUnverifiedRegistration("x", "h", "Name", null, null, Gender.Male));
    }

    [Fact]
    public void Lock_Blocks_Login()
    {
        var user = User.Create("lock@raceday.vn", "lockuser", "hash", "Lock User", RoleId, status: UserStatus.Active);
        user.ConfirmEmailVerification();
        user.ClearDomainEvents();

        user.Lock();

        Assert.Equal(UserStatus.Locked, user.Status);
        Assert.Contains(user.DomainEvents, e => e is UserLockedDomainEvent);
        Assert.Throws<DomainException>(() => user.EnsureCanLogin());
    }

    [Fact]
    public void Lock_When_Already_Locked_Is_Idempotent()
    {
        var user = User.Create("lock2@raceday.vn", "lock2", "hash", "Lock User", RoleId);
        user.Lock();
        user.ClearDomainEvents();

        user.Lock();

        Assert.Equal(UserStatus.Locked, user.Status);
        Assert.Empty(user.DomainEvents);
    }

    [Fact]
    public void Unlock_Unverified_Returns_Pending()
    {
        var user = User.Create("unlock@raceday.vn", "unlock", "hash", "Unlock User", RoleId);
        user.Lock();
        user.ClearDomainEvents();

        user.Unlock();

        Assert.Equal(UserStatus.Pending, user.Status);
        Assert.False(user.EmailVerified);
        Assert.Contains(user.DomainEvents, e => e is UserUnlockedDomainEvent);
    }

    [Fact]
    public void Unlock_Verified_Returns_Active()
    {
        var user = User.Create("unlock2@raceday.vn", "unlock2", "hash", "Unlock User", RoleId, status: UserStatus.Active);
        user.ConfirmEmailVerification();
        user.Lock();
        user.ClearDomainEvents();

        user.Unlock();

        Assert.Equal(UserStatus.Active, user.Status);
        user.EnsureCanLogin();
    }

    [Fact]
    public void EmailOtp_Expire_And_MaxAttempts()
    {
        var otp = EmailOtp.Issue("a@b.com", OtpPurpose.EmailVerification, "hash", ttl: TimeSpan.FromMinutes(-1));
        Assert.Throws<DomainException>(() => otp.EnsureCanVerify(DateTime.UtcNow));

        var otp2 = EmailOtp.Issue("a@b.com", OtpPurpose.PasswordReset, "hash");
        for (var i = 0; i < EmailOtp.MaxAttempts; i++)
        {
            otp2.RegisterFailedAttempt();
        }

        Assert.True(otp2.IsConsumed);
    }
}
