using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Packages;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Packages;

public sealed record PackageDurationOptionDto(
    Guid Id,
    string Name,
    int Months,
    decimal Price,
    string Status,
    int SortOrder,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record PackageCapacityOptionDto(
    Guid Id,
    string Name,
    int MaxParticipants,
    decimal AddOnFee,
    string Status,
    int SortOrder,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

internal static class PackageOptionDtoMapper
{
    public static PackageDurationOptionDto ToDto(PackageDurationOption d) => new(
        d.Id, d.Name, d.Months, d.Price,
        d.Status == PackageOptionStatus.Active ? "active" : "inactive",
        d.SortOrder, d.CreatedAtUtc, d.UpdatedAtUtc);

    public static PackageCapacityOptionDto ToDto(PackageCapacityOption c) => new(
        c.Id, c.Name, c.MaxParticipants, c.AddOnFee,
        c.Status == PackageOptionStatus.Active ? "active" : "inactive",
        c.SortOrder, c.CreatedAtUtc, c.UpdatedAtUtc);
}

// ── Duration options ──────────────────────────────────────────────

public sealed record ListPackageDurationsQuery : IRequest<IReadOnlyList<PackageDurationOptionDto>>;

public sealed class ListPackageDurationsQueryHandler(IPackageDurationOptionRepository repo)
    : IRequestHandler<ListPackageDurationsQuery, IReadOnlyList<PackageDurationOptionDto>>
{
    public async Task<IReadOnlyList<PackageDurationOptionDto>> Handle(ListPackageDurationsQuery request, CancellationToken cancellationToken)
        => (await repo.ListAllAsync(cancellationToken)).Select(PackageOptionDtoMapper.ToDto).ToList();
}

public sealed record CreatePackageDurationCommand(string Name, int Months, decimal Price, int? SortOrder)
    : IRequest<PackageDurationOptionDto>;

public sealed class CreatePackageDurationCommandValidator : AbstractValidator<CreatePackageDurationCommand>
{
    public CreatePackageDurationCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Months).GreaterThanOrEqualTo(1);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreatePackageDurationCommandHandler(
    IPackageDurationOptionRepository repo,
    IEventUnitOfWork unitOfWork) : IRequestHandler<CreatePackageDurationCommand, PackageDurationOptionDto>
{
    public async Task<PackageDurationOptionDto> Handle(CreatePackageDurationCommand request, CancellationToken cancellationToken)
    {
        if (await repo.ExistsByMonthsAsync(request.Months, cancellationToken))
            throw new DomainException($"Thời hạn {request.Months} tháng đã tồn tại.");

        var option = PackageDurationOption.Create(
            request.Name, request.Months, request.Price, request.SortOrder ?? request.Months);
        await repo.AddAsync(option, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PackageOptionDtoMapper.ToDto(option);
    }
}

public sealed record UpdatePackageDurationCommand(
    Guid Id,
    string? Name,
    decimal? Price,
    int? SortOrder,
    bool? Active) : IRequest<PackageDurationOptionDto>;

public sealed class UpdatePackageDurationCommandHandler(
    IPackageDurationOptionRepository repo,
    IEventUnitOfWork unitOfWork) : IRequestHandler<UpdatePackageDurationCommand, PackageDurationOptionDto>
{
    public async Task<PackageDurationOptionDto> Handle(UpdatePackageDurationCommand request, CancellationToken cancellationToken)
    {
        var option = await repo.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new DomainException("Không tìm thấy thời hạn gói.");

        option.Update(request.Name, request.Price, request.SortOrder);
        if (request.Active.HasValue)
        {
            if (request.Active.Value) option.Activate();
            else option.Deactivate();
        }

        repo.Update(option);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PackageOptionDtoMapper.ToDto(option);
    }
}

// ── Capacity options ──────────────────────────────────────────────

public sealed record ListPackageCapacitiesQuery : IRequest<IReadOnlyList<PackageCapacityOptionDto>>;

public sealed class ListPackageCapacitiesQueryHandler(IPackageCapacityOptionRepository repo)
    : IRequestHandler<ListPackageCapacitiesQuery, IReadOnlyList<PackageCapacityOptionDto>>
{
    public async Task<IReadOnlyList<PackageCapacityOptionDto>> Handle(ListPackageCapacitiesQuery request, CancellationToken cancellationToken)
        => (await repo.ListAllAsync(cancellationToken)).Select(PackageOptionDtoMapper.ToDto).ToList();
}

public sealed record CreatePackageCapacityCommand(string Name, int MaxParticipants, decimal AddOnFee, int? SortOrder)
    : IRequest<PackageCapacityOptionDto>;

public sealed class CreatePackageCapacityCommandValidator : AbstractValidator<CreatePackageCapacityCommand>
{
    public CreatePackageCapacityCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.MaxParticipants).GreaterThanOrEqualTo(1);
        RuleFor(x => x.AddOnFee).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreatePackageCapacityCommandHandler(
    IPackageCapacityOptionRepository repo,
    IEventUnitOfWork unitOfWork) : IRequestHandler<CreatePackageCapacityCommand, PackageCapacityOptionDto>
{
    public async Task<PackageCapacityOptionDto> Handle(CreatePackageCapacityCommand request, CancellationToken cancellationToken)
    {
        if (await repo.ExistsByCapAsync(request.MaxParticipants, cancellationToken))
            throw new DomainException($"Quy mô {request.MaxParticipants} VĐV đã tồn tại.");

        var option = PackageCapacityOption.Create(
            request.Name, request.MaxParticipants, request.AddOnFee, request.SortOrder ?? request.MaxParticipants);
        await repo.AddAsync(option, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PackageOptionDtoMapper.ToDto(option);
    }
}

public sealed record UpdatePackageCapacityCommand(
    Guid Id,
    string? Name,
    decimal? AddOnFee,
    int? SortOrder,
    bool? Active) : IRequest<PackageCapacityOptionDto>;

public sealed class UpdatePackageCapacityCommandHandler(
    IPackageCapacityOptionRepository repo,
    IEventUnitOfWork unitOfWork) : IRequestHandler<UpdatePackageCapacityCommand, PackageCapacityOptionDto>
{
    public async Task<PackageCapacityOptionDto> Handle(UpdatePackageCapacityCommand request, CancellationToken cancellationToken)
    {
        var option = await repo.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new DomainException("Không tìm thấy quy mô VĐV.");

        option.Update(request.Name, request.AddOnFee, request.SortOrder);
        if (request.Active.HasValue)
        {
            if (request.Active.Value) option.Activate();
            else option.Deactivate();
        }

        repo.Update(option);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PackageOptionDtoMapper.ToDto(option);
    }
}
