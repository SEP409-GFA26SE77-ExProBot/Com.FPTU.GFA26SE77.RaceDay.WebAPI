using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Application.EmergencyResponses;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Application.EmergencyResources;

public sealed record CreateEmergencyResourceRequest(
    string? ResourceCode,
    string? ResourceType,
    string? Name,
    string? Status,
    string? Notes);

public sealed record UpdateEmergencyResourceRequest(
    string? ResourceType,
    string? Name,
    string? Notes);

public sealed record CreateEmergencyResourceCommand(Guid EventId, CreateEmergencyResourceRequest Body)
    : IRequest<EmergencyResourceDto>;

public sealed record UpdateEmergencyResourceCommand(Guid EventId, Guid Id, UpdateEmergencyResourceRequest Body)
    : IRequest<EmergencyResourceDto>;

public sealed class CreateEmergencyResourceCommandValidator : AbstractValidator<CreateEmergencyResourceCommand>
{
    public CreateEmergencyResourceCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty().WithMessage("Tham chiếu sự kiện không hợp lệ.");
        RuleFor(x => x.Body).NotNull().WithMessage("Cần thông tin nguồn lực.");
        When(x => x.Body is not null, () =>
        {
            RuleFor(x => x.Body.ResourceCode)
                .Must(code => !string.IsNullOrWhiteSpace(code))
                .WithMessage("Mã nguồn lực không được để trống.");
            RuleFor(x => x.Body.ResourceCode)
                .Must(code => string.IsNullOrWhiteSpace(code) || code.Trim().Length <= CoordinatorFieldLengths.Code)
                .WithMessage("Mã nguồn lực tối đa 50 ký tự.");
            RuleFor(x => x.Body.Name)
                .Must(name => !string.IsNullOrWhiteSpace(name))
                .WithMessage("Tên nguồn lực không được để trống.");
            RuleFor(x => x.Body.Name)
                .Must(name => string.IsNullOrWhiteSpace(name) || name.Trim().Length <= CoordinatorFieldLengths.Name)
                .WithMessage("Tên nguồn lực tối đa 200 ký tự.");
            RuleFor(x => x.Body.ResourceType)
                .Must(type => EmergencyResponseEnumParser.IsDefinedName<EmergencyResourceType>(type))
                .WithMessage("Loại nguồn lực không hợp lệ.");
            RuleFor(x => x.Body.Status)
                .Must(status => EmergencyResponseEnumParser.IsDefinedName<EmergencyResourceStatus>(status))
                .WithMessage("Trạng thái nguồn lực không hợp lệ.");
        });
    }
}

public sealed class UpdateEmergencyResourceCommandValidator : AbstractValidator<UpdateEmergencyResourceCommand>
{
    public UpdateEmergencyResourceCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty().WithMessage("Tham chiếu sự kiện không hợp lệ.");
        RuleFor(x => x.Id).NotEmpty().WithMessage("Tham chiếu nguồn lực không hợp lệ.");
        RuleFor(x => x.Body).NotNull().WithMessage("Cần thông tin nguồn lực.");
        When(x => x.Body is not null, () =>
        {
            RuleFor(x => x.Body.Name)
                .Must(name => !string.IsNullOrWhiteSpace(name))
                .WithMessage("Tên nguồn lực không được để trống.");
            RuleFor(x => x.Body.Name)
                .Must(name => string.IsNullOrWhiteSpace(name) || name.Trim().Length <= CoordinatorFieldLengths.Name)
                .WithMessage("Tên nguồn lực tối đa 200 ký tự.");
            RuleFor(x => x.Body.ResourceType)
                .Must(type => EmergencyResponseEnumParser.IsDefinedName<EmergencyResourceType>(type))
                .WithMessage("Loại nguồn lực không hợp lệ.");
        });
    }
}

public sealed class CreateEmergencyResourceCommandHandler(
    IEmergencyResourceRepository resources,
    IRaceMonitoringUnitOfWork unitOfWork,
    IEventOrganizerAccessQuery access,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser) : IRequestHandler<CreateEmergencyResourceCommand, EmergencyResourceDto>
{
    public async Task<EmergencyResourceDto> Handle(
        CreateEmergencyResourceCommand request,
        CancellationToken cancellationToken)
    {
        await EmergencyResponseAccess.EnsureCanCoordinateEventAsync(
            request.EventId, access, sosOps, currentUser, cancellationToken);

        var body = request.Body ?? throw new DomainException("Cần thông tin nguồn lực.");
        var resource = EmergencyResource.Create(
            request.EventId,
            body.ResourceCode ?? string.Empty,
            RequiredEnum<EmergencyResourceType>(body.ResourceType, "Loại nguồn lực không hợp lệ."),
            body.Name ?? string.Empty,
            RequiredEnum<EmergencyResourceStatus>(body.Status, "Trạng thái nguồn lực không hợp lệ."),
            body.Notes);
        if (await resources.ExistsByEventAndCodeAsync(request.EventId, resource.ResourceCode, cancellationToken))
        {
            throw new DomainException("Mã nguồn lực đã tồn tại trong giải.");
        }

        resources.Add(resource);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return EmergencyResourceMapper.ToDto(resource);
    }

    internal static TEnum RequiredEnum<TEnum>(string? value, string message)
        where TEnum : struct, Enum
        => EmergencyResponseEnumParser.ParseOptional<TEnum>(value, message) ?? throw new DomainException(message);
}

public sealed class UpdateEmergencyResourceCommandHandler(
    IEmergencyResourceRepository resources,
    IRaceMonitoringUnitOfWork unitOfWork,
    IEventOrganizerAccessQuery access,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser) : IRequestHandler<UpdateEmergencyResourceCommand, EmergencyResourceDto>
{
    public async Task<EmergencyResourceDto> Handle(
        UpdateEmergencyResourceCommand request,
        CancellationToken cancellationToken)
    {
        var resource = await resources.GetTrackedByIdAsync(request.Id, cancellationToken)
            ?? throw new DomainException("Không tìm thấy nguồn lực.");
        if (resource.EventId is null)
        {
            throw new DomainException("Nguồn lực không thuộc giải.");
        }

        await EmergencyResponseAccess.EnsureCanCoordinateEventAsync(
            resource.EventId.Value, access, sosOps, currentUser, cancellationToken);
        if (resource.EventId != request.EventId)
        {
            throw new DomainException("Nguồn lực không thuộc giải này.");
        }

        var body = request.Body ?? throw new DomainException("Cần thông tin nguồn lực.");
        resource.UpdateDetails(
            CreateEmergencyResourceCommandHandler.RequiredEnum<EmergencyResourceType>(
                body.ResourceType,
                "Loại nguồn lực không hợp lệ."),
            body.Name,
            body.Notes);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return EmergencyResourceMapper.ToDto(resource);
    }
}
