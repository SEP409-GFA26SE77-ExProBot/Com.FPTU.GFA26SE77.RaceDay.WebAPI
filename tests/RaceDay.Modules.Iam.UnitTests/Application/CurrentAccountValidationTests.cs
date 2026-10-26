using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RaceDay.Modules.Iam.Api;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Domain.Roles;
using RaceDay.Modules.Iam.Domain.Users;

namespace RaceDay.Modules.Iam.UnitTests.Application;

public class CurrentAccountValidationTests
{
    private static TokenValidatedContext Context(IUserRepository users, IRoleRepository roles, string subject)
    {
        var services = new ServiceCollection().AddSingleton(users).AddSingleton(roles).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        return new TokenValidatedContext(http,
            new AuthenticationScheme("Bearer", null, typeof(JwtBearerHandler)), new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, subject),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim("role", "Admin")
            ], "Bearer"))
        };
    }

    [Fact]
    public async Task Validate_RoleChanged_RemovesStaleAdminClaims()
    {
        var role = Role.Create("User");
        var user = User.Create("runner@test.local", "runner", "hash", "Runner", role.Id, status: UserStatus.Active);
        user.MarkEmailVerified();
        var users = Substitute.For<IUserRepository>();
        var roles = Substitute.For<IRoleRepository>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        roles.GetByIdAsync(role.Id, Arg.Any<CancellationToken>()).Returns(role);
        var context = Context(users, roles, user.Id.ToString());

        await CurrentAccountValidation.ValidateAsync(context);

        Assert.False(context.Principal!.IsInRole("Admin"));
        Assert.True(context.Principal.IsInRole("User"));
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task Validate_LockedUser_FailsAuthentication()
    {
        var user = User.Create("locked@test.local", "locked", "hash", "Locked", Guid.NewGuid());
        user.MarkEmailVerified();
        user.Lock();
        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var context = Context(users, Substitute.For<IRoleRepository>(), user.Id.ToString());

        await CurrentAccountValidation.ValidateAsync(context);

        Assert.NotNull(context.Result?.Failure);
    }

    [Fact]
    public async Task Validate_DeletedUser_FailsAuthentication()
    {
        var context = Context(Substitute.For<IUserRepository>(), Substitute.For<IRoleRepository>(), Guid.NewGuid().ToString());
        await CurrentAccountValidation.ValidateAsync(context);
        Assert.NotNull(context.Result?.Failure);
    }

    [Fact]
    public async Task Validate_InvalidSubject_FailsAuthentication()
    {
        var context = Context(Substitute.For<IUserRepository>(), Substitute.For<IRoleRepository>(), "not-a-guid");
        await CurrentAccountValidation.ValidateAsync(context);
        Assert.NotNull(context.Result?.Failure);
    }
}
