using NSubstitute;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Application.Users;
using RaceDay.Modules.Iam.Application.Users.UpdateMe;
using RaceDay.Modules.Iam.Application.Users.UpdateUser;
using RaceDay.Modules.Iam.Domain.Roles;
using RaceDay.Modules.Iam.Domain.Users;
using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.Iam.UnitTests.Application;

public class ProfileIdentityNumberTests
{
    private const string OriginalIdentity = "000123456789";

    private static User ExistingUser(Guid roleId)
    {
        var user = User.Create("runner@test.local", "runner", "hash", "Runner", roleId);
        user.UpdateProfile(user.FullName, null, null, Gender.Unknown, null, null, null, OriginalIdentity);
        return user;
    }

    [Theory]
    [InlineData("000987654321", "000987654321")]
    [InlineData("  b1234567  ", "B1234567")]
    [InlineData(null, OriginalIdentity)]
    [InlineData("", null)]
    public async Task UpdateMe_IdentityNumber_UpdatesNormalizesPreservesOrClears(string? input, string? expected)
    {
        var role = Role.Create("User");
        var user = ExistingUser(role.Id);
        var current = Substitute.For<ICurrentUserAccessor>();
        current.UserId.Returns(user.Id);
        var users = Substitute.For<IUserRepository>();
        users.GetByIdWithProfileAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var roles = Substitute.For<IRoleRepository>();
        roles.GetByIdAsync(role.Id, Arg.Any<CancellationToken>()).Returns(role);
        var uow = Substitute.For<IUnitOfWork>();
        var handler = new UpdateMyProfileCommandHandler(current, users, roles, uow, Substitute.For<IEventBus>());

        var result = await handler.Handle(new UpdateMyProfileCommand("Runner", null, null, GenderDto.Unknown, IdNumber: input), CancellationToken.None);

        Assert.Equal(expected, user.IdNumber);
        Assert.Equal(expected, result.IdNumber);
        users.Received(1).Update(user);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("000987654321", "000987654321")]
    [InlineData("  b1234567  ", "B1234567")]
    [InlineData(null, OriginalIdentity)]
    [InlineData("", null)]
    public async Task UpdateUser_IdentityNumber_UpdatesNormalizesPreservesOrClears(string? input, string? expected)
    {
        var role = Role.Create("User");
        var user = ExistingUser(role.Id);
        var users = Substitute.For<IUserRepository>();
        users.GetByIdWithProfileAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var roles = Substitute.For<IRoleRepository>();
        roles.GetByIdAsync(role.Id, Arg.Any<CancellationToken>()).Returns(role);
        var handler = new UpdateUserCommandHandler(users, roles, Substitute.For<IUnitOfWork>(), Substitute.For<IEventBus>());

        await handler.Handle(new UpdateUserCommand(user.Id, user.Username, user.FullName, null, null,
            GenderDto.Unknown, role.Id, UserStatusDto.Active, null, null, null, input), CancellationToken.None);

        Assert.Equal(expected, user.IdNumber);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("000123456", true)]
    [InlineData("000123456789", true)]
    [InlineData("b1234567", true)]
    [InlineData("12345678", false)]
    [InlineData("AB-12345", false)]
    [InlineData("A123456789012345678901234567890", false)]
    public void ProfileValidators_IdentityNumber_ApplyTheSameRules(string? input, bool expected)
    {
        var self = new UpdateMyProfileCommand("Runner", null, null, GenderDto.Unknown, IdNumber: input);
        var admin = new UpdateUserCommand(Guid.NewGuid(), "runner", "Runner", null, null,
            GenderDto.Unknown, Guid.NewGuid(), UserStatusDto.Active, null, null, null, input);

        Assert.Equal(expected, new UpdateMyProfileCommandValidator().Validate(self).IsValid);
        Assert.Equal(expected, new UpdateUserCommandValidator().Validate(admin).IsValid);
    }
}
