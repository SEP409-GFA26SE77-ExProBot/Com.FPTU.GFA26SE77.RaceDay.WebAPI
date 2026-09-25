using FluentValidation;
using MediatR;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.Application.Users.UpdateUser;

public sealed record UpdateUserRequest(
    string FullName,
    string? Phone,
    DateOnly? DateOfBirth,
    GenderDto Gender,
    Guid RoleId,
    UserStatusDto Status,
    string? ProfileDescription,
    string? ContactEmail,
    string? ContactPhone);

public sealed record UpdateUserCommand(
    Guid UserId,
    string FullName,
    string? Phone,
    DateOnly? DateOfBirth,
    GenderDto Gender,
    Guid RoleId,
    UserStatusDto Status,
    string? ProfileDescription,
    string? ContactEmail,
    string? ContactPhone) : IRequest;

public sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.Phone).MaximumLength(20).When(x => x.Phone is not null);
        RuleFor(x => x.Gender).IsInEnum();
        RuleFor(x => x.Status).IsInEnum();
    }
}

public sealed class UpdateUserCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    IUnitOfWork unitOfWork,
    IEventBus eventBus) : IRequestHandler<UpdateUserCommand>
{
    public async Task Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdWithProfileAsync(request.UserId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy người dùng.");

        _ = await roles.GetByIdAsync(request.RoleId, cancellationToken)
            ?? throw new DomainException("Role không tồn tại.");

        user.UpdateProfile(
            request.FullName,
            request.Phone,
            request.DateOfBirth,
            request.Gender.ToDomain(),
            request.ProfileDescription,
            request.ContactEmail,
            request.ContactPhone);
        user.ChangeRole(request.RoleId);
        user.SetStatus(request.Status.ToDomain());

        users.Update(user);

        await eventBus.PublishAsync(
            new UserUpdatedIntegrationEvent(user.Id, user.Email, user.FullName),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
