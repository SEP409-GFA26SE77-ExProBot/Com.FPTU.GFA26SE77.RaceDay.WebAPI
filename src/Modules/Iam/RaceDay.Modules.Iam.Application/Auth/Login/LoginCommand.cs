using FluentValidation;
using MediatR;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.Application.Auth.Login;

public sealed record LoginCommand(string Username, string Password) : IRequest<LoginResult>;

public sealed record LoginResult(
    Guid UserId,
    string Email,
    string Username,
    string FullName,
    string Role,
    string AccessToken,
    bool MustChangePassword = false);

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class LoginCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    IEventBus eventBus,
    IUnitOfWork unitOfWork) : IRequestHandler<LoginCommand, LoginResult>
{
    public async Task<LoginResult> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByUsernameAsync(request.Username, cancellationToken)
            ?? throw new DomainException("Tên đăng nhập hoặc mật khẩu không đúng.");

        user.EnsureCanLogin();

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new DomainException("Tên đăng nhập hoặc mật khẩu không đúng.");
        }

        var role = await roles.GetByIdAsync(user.RoleId, cancellationToken)
            ?? throw new DomainException("Role của tài khoản không hợp lệ.");

        var token = jwtTokenService.CreateAccessToken(user, role.RoleName);

        await eventBus.PublishAsync(new UserLoggedInIntegrationEvent(user.Id, user.Email), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new LoginResult(
            user.Id,
            user.Email,
            user.Username,
            user.FullName,
            role.RoleName,
            token,
            user.MustChangePassword);
    }
}
