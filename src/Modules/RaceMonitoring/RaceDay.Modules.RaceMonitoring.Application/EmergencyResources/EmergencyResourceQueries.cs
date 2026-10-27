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

public sealed record EmergencyResourceDto(
    Guid Id,
    Guid EventId,
    string ResourceCode,
    string ResourceType,
    string Name,
    string Status,
    string? Notes);

public sealed record PagedEmergencyResourcesResult(
    IReadOnlyList<EmergencyResourceDto> Items,
    int Total,
    int Page,
    int PageSize);

public sealed record ListEmergencyResourcesQuery(
    Guid EventId,
    string? Status,
    string? ResourceType,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedEmergencyResourcesResult>;

internal static class EmergencyResourceMapper
{
    public static EmergencyResourceDto ToDto(EmergencyResource resource)
    {
        if (resource.EventId is null)
        {
            throw new DomainException("Nguồn lực không thuộc giải.");
        }

        return new EmergencyResourceDto(
            resource.Id,
            resource.EventId.Value,
            resource.ResourceCode,
            resource.ResourceType.ToString(),
            resource.Name,
            resource.Status.ToString(),
            resource.Notes);
    }
}

public sealed class ListEmergencyResourcesQueryValidator : AbstractValidator<ListEmergencyResourcesQuery>
{
    public ListEmergencyResourcesQueryValidator()
    {
        RuleFor(x => x.EventId).NotEmpty().WithMessage("Tham chiếu sự kiện không hợp lệ.");
    }
}

public sealed class ListEmergencyResourcesQueryHandler(
    IEmergencyResourceRepository resources,
    IEventOrganizerAccessQuery access,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListEmergencyResourcesQuery, PagedEmergencyResourcesResult>
{
    public async Task<PagedEmergencyResourcesResult> Handle(
        ListEmergencyResourcesQuery request,
        CancellationToken cancellationToken)
    {
        await EmergencyResponseAccess.EnsureCanCoordinateEventAsync(
            request.EventId, access, sosOps, currentUser, cancellationToken);

        var status = EmergencyResponseEnumParser.ParseOptional<EmergencyResourceStatus>(
            request.Status, "Trạng thái nguồn lực không hợp lệ.");
        var resourceType = EmergencyResponseEnumParser.ParseOptional<EmergencyResourceType>(
            request.ResourceType, "Loại nguồn lực không hợp lệ.");
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? 20 : request.PageSize;
        var (items, total) = await resources.ListByEventAsync(
            request.EventId, status, resourceType, page, pageSize, cancellationToken);
        return new PagedEmergencyResourcesResult(
            items.Select(EmergencyResourceMapper.ToDto).ToList(),
            total,
            page,
            pageSize);
    }
}
