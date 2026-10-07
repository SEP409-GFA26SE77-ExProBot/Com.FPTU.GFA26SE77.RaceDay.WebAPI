using NSubstitute;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Application.Auth.ChangePassword;
using RaceDay.Modules.Iam.Application.Auth.ForgotPassword;
using RaceDay.Modules.Iam.Application.Auth.Login;
using RaceDay.Modules.Iam.Application.Auth.Register;
using RaceDay.Modules.Iam.Application.Auth.ResendOtp;
using RaceDay.Modules.Iam.Application.Auth.VerifyEmail;
using RaceDay.Modules.Iam.Application.Users;
using RaceDay.Modules.Iam.Application.Users.DeleteUser;
using RaceDay.Modules.Iam.Application.Users.GetUser;
using RaceDay.Modules.Iam.Application.Users.UpdateUser;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Modules.Iam.Domain.Roles;
using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.UnitTests.Application;

public class AuthAndCrudHandlerTests
{
    private static readonly Guid RoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static User ActiveUser(string email = "a@raceday.vn", string username = "auser")
    {
        var user = User.Create(email, username, "hash", "A", RoleId, status: UserStatus.Active);
        user.ConfirmEmailVerification();
        user.ClearDomainEvents();
        return user;
    }

    private static LoginCommandHandler CreateLoginHandler(
        IUserRepository users,
        IRoleRepository? roles = null,
        IPasswordHasher? hasher = null,
        IJwtTokenService? jwt = null,
        IEventBus? bus = null,
        IUnitOfWork? uow = null)
        => new(
            users,
            roles ?? Substitute.For<IRoleRepository>(),
            hasher ?? Substitute.For<IPasswordHasher>(),
            jwt ?? Substitute.For<IJwtTokenService>(),
            bus ?? Substitute.For<IEventBus>(),
            uow ?? Substitute.For<IUnitOfWork>());

    [Fact]
    public async Task Login_WrongUsername_Throws_Generic_Message()
    {
        var users = Substitute.For<IUserRepository>();
        users.GetByUsernameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            CreateLoginHandler(users).Handle(new LoginCommand("missing", "pass"), CancellationToken.None));
        Assert.Equal("Tên đăng nhập hoặc mật khẩu không đúng.", ex.Message);
    }

    [Fact]
    public async Task Login_Pending_Unverified_Throws()
    {
        var user = User.Create("p@raceday.vn", "pending", "hash", "P", RoleId);
        var users = Substitute.For<IUserRepository>();
        users.GetByUsernameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            CreateLoginHandler(users).Handle(new LoginCommand("pending", "hash"), CancellationToken.None));
        Assert.Contains("chưa xác thực", ex.Message);
    }

    [Fact]
    public async Task Login_WrongPassword_Throws_Generic_Message()
    {
        var user = ActiveUser();
        var users = Substitute.For<IUserRepository>();
        var hasher = Substitute.For<IPasswordHasher>();
        users.GetByUsernameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        hasher.Verify("bad", "hash").Returns(false);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            CreateLoginHandler(users, hasher: hasher).Handle(new LoginCommand("auser", "bad"), CancellationToken.None));
        Assert.Equal("Tên đăng nhập hoặc mật khẩu không đúng.", ex.Message);
    }

    [Fact]
    public async Task Login_LockedUser_Throws()
    {
        var user = ActiveUser();
        user.Lock();
        var users = Substitute.For<IUserRepository>();
        users.GetByUsernameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            CreateLoginHandler(users).Handle(new LoginCommand("auser", "hash"), CancellationToken.None));
        Assert.Contains("khóa", ex.Message);
    }

    [Fact]
    public async Task Login_Success_Enqueues_Event_Then_Saves()
    {
        var user = ActiveUser();
        var role = Role.Restore(RoleId, "User", null);
        var users = Substitute.For<IUserRepository>();
        var roles = Substitute.For<IRoleRepository>();
        var hasher = Substitute.For<IPasswordHasher>();
        var jwt = Substitute.For<IJwtTokenService>();
        var bus = Substitute.For<IEventBus>();
        var uow = Substitute.For<IUnitOfWork>();

        users.GetByUsernameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        hasher.Verify("Password1!", "hash").Returns(true);
        roles.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(role);
        jwt.CreateAccessToken(user, "User").Returns("token-xyz");

        var result = await CreateLoginHandler(users, roles, hasher, jwt, bus, uow)
            .Handle(new LoginCommand("auser", "Password1!"), CancellationToken.None);

        Assert.Equal("token-xyz", result.AccessToken);
        Assert.Equal("auser", result.Username);
        await bus.Received(1).PublishAsync(Arg.Any<UserLoggedInIntegrationEvent>(), Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Register_Creates_Pending_And_Sends_Otp()
    {
        var users = Substitute.For<IUserRepository>();
        var roles = Substitute.For<IRoleRepository>();
        var hasher = Substitute.For<IPasswordHasher>();
        var otp = Substitute.For<IEmailOtpService>();
        var uow = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IEventBus>();

        users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        users.GetByUsernameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        roles.GetByNameAsync("User", Arg.Any<CancellationToken>()).Returns(Role.Restore(RoleId, "User", null));
        hasher.Hash(Arg.Any<string>()).Returns("hashed");

        var result = await new RegisterUserCommandHandler(users, roles, hasher, otp, uow, bus).Handle(
            new RegisterUserCommand("n@raceday.vn", "newuser", "Password1!", "New", null, null, GenderDto.Male),
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.UserId);
        Assert.Equal("newuser", result.Username);
        await otp.Received(1).IssueAndSendAsync(
            "n@raceday.vn",
            "newuser",
            Arg.Any<Guid?>(),
            OtpPurpose.EmailVerification,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Register_DuplicateVerifiedEmail_Throws()
    {
        var users = Substitute.For<IUserRepository>();
        users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ActiveUser());

        var handler = new RegisterUserCommandHandler(
            users, Substitute.For<IRoleRepository>(), Substitute.For<IPasswordHasher>(),
            Substitute.For<IEmailOtpService>(), Substitute.For<IUnitOfWork>(), Substitute.For<IEventBus>());

        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new RegisterUserCommand("a@raceday.vn", "auser", "Password1!", "A", null, null, GenderDto.Male),
            CancellationToken.None));
    }

    [Fact]
    public async Task Register_UnverifiedEmail_Replaces_And_Sends_Otp()
    {
        var pending = User.Create("p@raceday.vn", "oldname", "old-hash", "Old Name", RoleId);
        var users = Substitute.For<IUserRepository>();
        var hasher = Substitute.For<IPasswordHasher>();
        var otp = Substitute.For<IEmailOtpService>();
        var uow = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IEventBus>();

        users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(pending);
        users.GetByUsernameAsync("newname", Arg.Any<CancellationToken>()).Returns((User?)null);
        hasher.Hash("Password1!").Returns("new-hash");

        var result = await new RegisterUserCommandHandler(
                users, Substitute.For<IRoleRepository>(), hasher, otp, uow, bus)
            .Handle(
                new RegisterUserCommand(
                    "p@raceday.vn", "newname", "Password1!", "New Name", "0901111222",
                    new DateOnly(2001, 2, 3), GenderDto.Female),
                CancellationToken.None);

        Assert.Equal(pending.Id, result.UserId);
        Assert.Equal("newname", result.Username);
        Assert.Equal("newname", pending.Username);
        Assert.Equal("New Name", pending.FullName);
        Assert.Equal("new-hash", pending.PasswordHash);
        Assert.Equal(UserStatus.Pending, pending.Status);
        Assert.False(pending.EmailVerified);
        users.Received(1).Update(pending);
        await otp.Received(1).IssueAndSendAsync(
            "p@raceday.vn",
            "newname",
            pending.Id,
            OtpPurpose.EmailVerification,
            Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await users.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyEmail_Success_Activates()
    {
        var user = User.Create("v@raceday.vn", "verify", "hash", "V", RoleId);
        var users = Substitute.For<IUserRepository>();
        var otp = Substitute.For<IEmailOtpService>();
        var uow = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IEventBus>();
        users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        otp.VerifyAsync(Arg.Any<string>(), OtpPurpose.EmailVerification, "123456", Arg.Any<CancellationToken>())
            .Returns(EmailOtp.Issue(user.Email, OtpPurpose.EmailVerification, "h", user.Id));

        await new VerifyEmailCommandHandler(users, otp, uow, bus)
            .Handle(new VerifyEmailCommand("v@raceday.vn", "123456"), CancellationToken.None);

        Assert.True(user.EmailVerified);
        Assert.Equal(UserStatus.Active, user.Status);
        await bus.Received(1).PublishAsync(Arg.Any<UserEmailVerifiedIntegrationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_Does_Not_Throw()
    {
        var users = Substitute.For<IUserRepository>();
        var otp = Substitute.For<IEmailOtpService>();
        users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        await new ForgotPasswordCommandHandler(users, otp)
            .Handle(new ForgotPasswordCommand("missing@raceday.vn"), CancellationToken.None);

        await otp.DidNotReceive().IssueAndSendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<OtpPurpose>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForgotPassword_KnownEmail_Sends_PasswordReset_Otp()
    {
        var user = ActiveUser("f@raceday.vn", "fuser");
        var users = Substitute.For<IUserRepository>();
        var otp = Substitute.For<IEmailOtpService>();
        users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);

        await new ForgotPasswordCommandHandler(users, otp)
            .Handle(new ForgotPasswordCommand("f@raceday.vn"), CancellationToken.None);

        await otp.Received(1).IssueAndSendAsync(
            "f@raceday.vn",
            "fuser",
            user.Id,
            OtpPurpose.PasswordReset,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetPasswordWithOtp_Success_Updates_Password()
    {
        var user = ActiveUser("r@raceday.vn", "ruser");
        var users = Substitute.For<IUserRepository>();
        var otp = Substitute.For<IEmailOtpService>();
        var hasher = Substitute.For<IPasswordHasher>();
        var uow = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IEventBus>();

        users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        otp.VerifyAsync(Arg.Any<string>(), OtpPurpose.PasswordReset, "654321", Arg.Any<CancellationToken>())
            .Returns(EmailOtp.Issue(user.Email, OtpPurpose.PasswordReset, "h", user.Id));
        hasher.Hash("NewPass1!").Returns("new-hash");

        await new ResetPasswordWithOtpCommandHandler(users, otp, hasher, uow, bus)
            .Handle(new ResetPasswordWithOtpCommand("r@raceday.vn", "654321", "NewPass1!"), CancellationToken.None);

        Assert.Equal("new-hash", user.PasswordHash);
        users.Received(1).Update(user);
        await bus.Received(1).PublishAsync(Arg.Any<PasswordResetIntegrationEvent>(), Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendOtp_Pending_Sends_Verification()
    {
        var user = User.Create("s@raceday.vn", "suser", "hash", "S", RoleId);
        var users = Substitute.For<IUserRepository>();
        var otp = Substitute.For<IEmailOtpService>();
        users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);

        await new ResendEmailVerificationOtpCommandHandler(users, otp)
            .Handle(new ResendEmailVerificationOtpCommand("s@raceday.vn"), CancellationToken.None);

        await otp.Received(1).IssueAndSendAsync(
            "s@raceday.vn",
            "suser",
            user.Id,
            OtpPurpose.EmailVerification,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendOtp_AlreadyVerified_Throws()
    {
        var user = ActiveUser("done@raceday.vn", "doneuser");
        var users = Substitute.For<IUserRepository>();
        users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);

        await Assert.ThrowsAsync<DomainException>(() =>
            new ResendEmailVerificationOtpCommandHandler(users, Substitute.For<IEmailOtpService>())
                .Handle(new ResendEmailVerificationOtpCommand("done@raceday.vn"), CancellationToken.None));
    }

    [Fact]
    public async Task ChangePassword_Unauthenticated_Throws()
    {
        var current = Substitute.For<ICurrentUserAccessor>();
        current.UserId.Returns((Guid?)null);
        var handler = new ChangePasswordCommandHandler(
            current, Substitute.For<IUserRepository>(), Substitute.For<IPasswordHasher>(),
            Substitute.For<IUnitOfWork>(), Substitute.For<IEventBus>());

        await Assert.ThrowsAsync<DomainException>(() =>
            handler.Handle(new ChangePasswordCommand("old", "Password1!"), CancellationToken.None));
    }

    [Fact]
    public async Task GetUser_NotFound_Throws()
    {
        var users = Substitute.For<IUserRepository>();
        users.GetByIdWithProfileAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        await Assert.ThrowsAsync<DomainException>(() =>
            new GetUserByIdQueryHandler(users, Substitute.For<IRoleRepository>())
                .Handle(new GetUserByIdQuery(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateUser_NotFound_Throws()
    {
        var users = Substitute.For<IUserRepository>();
        users.GetByIdWithProfileAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        await Assert.ThrowsAsync<DomainException>(() =>
            new UpdateUserCommandHandler(users, Substitute.For<IRoleRepository>(), Substitute.For<IUnitOfWork>(), Substitute.For<IEventBus>())
                .Handle(new UpdateUserCommand(Guid.NewGuid(), "uname", "N", null, null, GenderDto.Male, RoleId, UserStatusDto.Active, null, null, null),
                    CancellationToken.None));
    }

    [Fact]
    public async Task DeleteUser_Success_Publishes_Then_Saves()
    {
        var user = ActiveUser("del@raceday.vn", "deluser");
        var users = Substitute.For<IUserRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IEventBus>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        await new DeleteUserCommandHandler(users, uow, bus).Handle(new DeleteUserCommand(user.Id), CancellationToken.None);

        users.Received(1).Remove(user);
        await bus.Received(1).PublishAsync(Arg.Any<UserDeletedIntegrationEvent>(), Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
