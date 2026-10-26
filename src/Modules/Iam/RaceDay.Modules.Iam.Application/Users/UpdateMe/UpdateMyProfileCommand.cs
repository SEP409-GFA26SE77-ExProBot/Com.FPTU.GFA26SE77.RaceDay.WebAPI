using FluentValidation;
using MediatR;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Application.Users;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Application.Validation;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.Application.Users.UpdateMe;

public sealed record UpdateMyProfileCommand(
    string FullName,
    string? Phone,
    DateOnly? DateOfBirth,
    GenderDto Gender,
    string? AvatarUrl = null,
    string? IdNumber = null) : IRequest<UserDto>;

public sealed class UpdateMyProfileCommandValidator : AbstractValidator<UpdateMyProfileCommand>
{
    public UpdateMyProfileCommandValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Họ tên không được để trống.")
            .MinimumLength(2).WithMessage("Họ tên tối thiểu 2 ký tự.")
            .MaximumLength(100).WithMessage("Họ tên tối đa 100 ký tự.");
        RuleFor(x => x.Phone).MaximumLength(20).OptionalVietnamPhone();
        RuleFor(x => x.DateOfBirth).DateOfBirth();
        RuleFor(x => x.Gender).IsInEnum().WithMessage("Giới tính không hợp lệ.");
        RuleFor(x => x.AvatarUrl)
            .MaximumLength(500)
            .OptionalHttpUrl()
            .When(x => !string.IsNullOrWhiteSpace(x.AvatarUrl));
        RuleFor(x => x.IdNumber)
            .MaximumLength(30)
            .OptionalIdNumber();
    }
}

public sealed class UpdateMyProfileCommandHandler(
    ICurrentUserAccessor currentUser,
    IUserRepository users,
    IRoleRepository roles,
    IUnitOfWork unitOfWork,
    IEventBus eventBus) : IRequestHandler<UpdateMyProfileCommand, UserDto>
{
    public async Task<UserDto> Handle(UpdateMyProfileCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new DomainException("Bạn cần đăng nhập.");

        var user = await users.GetByIdWithProfileAsync(userId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy người dùng.");

        var phone = string.IsNullOrWhiteSpace(request.Phone)
            ? null
            : InputRules.NormalizeOptionalPhoneOrEmpty(request.Phone);
        if (phone is { Length: 0 })
        {
            phone = null;
        }

        user.UpdateProfile(
            request.FullName,
            phone,
            request.DateOfBirth,
            request.Gender.ToDomain(),
            user.Profile?.Description,
            user.Email,
            phone,
            request.IdNumber is null ? user.IdNumber : InputRules.NormalizeIdNumber(request.IdNumber));

        if (request.AvatarUrl is not null)
        {
            user.SetAvatarUrl(request.AvatarUrl);
        }

        users.Update(user);

        await eventBus.PublishAsync(
            new UserUpdatedIntegrationEvent(user.Id, user.Email, user.Username, user.FullName),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var role = await roles.GetByIdAsync(user.RoleId, cancellationToken);
        return UserDtoMapper.ToDto(user, role?.RoleName);
    }
}
