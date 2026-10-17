using FluentValidation.TestHelper;
using RaceDay.Modules.Iam.Application.Users;
using RaceDay.Modules.Iam.Application.Users.CreateUser;
using RaceDay.Modules.Iam.Application.Users.LockUser;

namespace RaceDay.Modules.Iam.UnitTests.Application;

public class CommandValidatorTests
{
    [Fact]
    public void CreateUser_InvalidEmail_Fails()
    {
        var result = new CreateUserCommandValidator().TestValidate(
            new CreateUserCommand("bad", "uname", "Password1!", "Name", Guid.NewGuid(), null, null, GenderDto.Male, UserStatusDto.Active));
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void CreateUser_ShortPassword_Fails()
    {
        var result = new CreateUserCommandValidator().TestValidate(
            new CreateUserCommand("a@b.com", "uname", "short", "Name", Guid.NewGuid(), null, null, GenderDto.Male, UserStatusDto.Active));
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void CreateUser_InvalidUsername_Fails()
    {
        var result = new CreateUserCommandValidator().TestValidate(
            new CreateUserCommand("a@b.com", "bad name!", "Password1!", "Name", Guid.NewGuid(), null, null, GenderDto.Male, UserStatusDto.Active));
        result.ShouldHaveValidationErrorFor(x => x.Username);
    }

    [Fact]
    public void LockUser_EmptyId_Fails()
    {
        var result = new LockUserCommandValidator().TestValidate(new LockUserCommand(Guid.Empty));
        result.ShouldHaveValidationErrorFor(x => x.UserId);
    }
}
