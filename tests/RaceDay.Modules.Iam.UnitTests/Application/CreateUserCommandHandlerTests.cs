using NSubstitute;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Application.Users;
using RaceDay.Modules.Iam.Application.Users.CreateUser;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Modules.Iam.Domain.Roles;
using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.UnitTests.Application;

public class CreateUserCommandHandlerTests
{
    private static readonly Guid RoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Handle_Success_Adds_User_And_Publishes_Event()
    {
        var users = Substitute.For<IUserRepository>();
        var roles = Substitute.For<IRoleRepository>();
        var hasher = Substitute.For<IPasswordHasher>();
        var uow = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IEventBus>();

        users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        users.GetByUsernameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        roles.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(Role.Restore(RoleId, "User", null));
        hasher.Hash("Password1!").Returns("hashed");

        var handler = new CreateUserCommandHandler(users, roles, hasher, uow, bus);
        var id = await handler.Handle(
            new CreateUserCommand("new@raceday.vn", "newuser", "Password1!", "New User", RoleId, null, null, GenderDto.Male, UserStatusDto.Active),
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
        await users.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Any<UserCreatedIntegrationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateEmail_Throws()
    {
        var users = Substitute.For<IUserRepository>();
        var roles = Substitute.For<IRoleRepository>();
        var existing = User.Create("dup@raceday.vn", "dupuser", "h", "Dup", RoleId);
        users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(existing);

        var handler = new CreateUserCommandHandler(users, roles, Substitute.For<IPasswordHasher>(), Substitute.For<IUnitOfWork>(), Substitute.For<IEventBus>());

        var ex = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new CreateUserCommand("dup@raceday.vn", "other", "Password1!", "Dup", RoleId, null, null, GenderDto.Unknown, UserStatusDto.Active),
            CancellationToken.None));

        Assert.Contains("Email đã được đăng ký", ex.Message);
    }

    [Fact]
    public async Task Handle_MissingRole_Throws()
    {
        var users = Substitute.For<IUserRepository>();
        var roles = Substitute.For<IRoleRepository>();
        users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        users.GetByUsernameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        roles.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Role?)null);

        var handler = new CreateUserCommandHandler(users, roles, Substitute.For<IPasswordHasher>(), Substitute.For<IUnitOfWork>(), Substitute.For<IEventBus>());

        var ex = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new CreateUserCommand("x@raceday.vn", "xuser", "Password1!", "X", RoleId, null, null, GenderDto.Unknown, UserStatusDto.Active),
            CancellationToken.None));

        Assert.Contains("Role không tồn tại", ex.Message);
    }
}
