using NSubstitute;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Application.Users.LockUser;
using RaceDay.Modules.Iam.Application.Users.UnlockUser;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Modules.Iam.Domain.Roles;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.UnitTests.Application;

public class LockUnlockUserCommandHandlerTests
{
    private static readonly Guid RoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Role AdminRole = Role.Create("Admin");
    private static readonly User Admin = User.Create("admin@test.local", "testadmin", "hash", "Admin", AdminRole.Id);
    private static readonly Guid ActorId = Admin.Id;
    private static ICurrentUserAccessor Actor()
    {
        var actor = Substitute.For<ICurrentUserAccessor>();
        actor.UserId.Returns(ActorId);
        return actor;
    }
    private static IRoleRepository Roles(IUserRepository users)
    {
        var adminRole = AdminRole;
        var roles = Substitute.For<IRoleRepository>();
        roles.GetByIdAsync(adminRole.Id, Arg.Any<CancellationToken>()).Returns(adminRole);
        roles.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(Role.Restore(RoleId, "User", null));
        users.GetByIdAsync(ActorId, Arg.Any<CancellationToken>()).Returns(Admin);
        return roles;
    }

    [Fact]
    public async Task Lock_NotFound_Throws()
    {
        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        var handler = new LockUserCommandHandler(users, Roles(users), Actor(), Substitute.For<IUnitOfWork>(), Substitute.For<IEventBus>());

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            handler.Handle(new LockUserCommand(Guid.NewGuid()), CancellationToken.None));
        Assert.Contains("Không tìm thấy", ex.Message);
    }

    [Fact]
    public async Task Lock_Success_Publishes_Event()
    {
        var user = User.Create("a@raceday.vn", "auser", "h", "A", RoleId);
        var users = Substitute.For<IUserRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IEventBus>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        await new LockUserCommandHandler(users, Roles(users), Actor(), uow, bus).Handle(new LockUserCommand(user.Id), CancellationToken.None);

        Assert.Equal(UserStatus.Locked, user.Status);
        users.Received(1).Update(user);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Any<UserLockedIntegrationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unlock_NotFound_Throws()
    {
        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        var handler = new UnlockUserCommandHandler(users, Roles(users), Actor(), Substitute.For<IUnitOfWork>(), Substitute.For<IEventBus>());

        await Assert.ThrowsAsync<DomainException>(() =>
            handler.Handle(new UnlockUserCommand(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Unlock_Success_Publishes_Event()
    {
        var user = User.Create("b@raceday.vn", "buser", "h", "B", RoleId);
        user.Lock();
        var users = Substitute.For<IUserRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IEventBus>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        await new UnlockUserCommandHandler(users, Roles(users), Actor(), uow, bus).Handle(new UnlockUserCommand(user.Id), CancellationToken.None);

        Assert.Equal(UserStatus.Pending, user.Status);
        await bus.Received(1).PublishAsync(Arg.Any<UserUnlockedIntegrationEvent>(), Arg.Any<CancellationToken>());
    }
}
