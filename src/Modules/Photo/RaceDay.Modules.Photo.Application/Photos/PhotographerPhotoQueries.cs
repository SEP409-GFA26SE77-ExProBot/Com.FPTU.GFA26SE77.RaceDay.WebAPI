using MediatR;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Photo.Application.Abstractions;
using RaceDay.Modules.Photo.Domain;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Photo.Application.Photos;

public sealed record ListPhotographerPhotosQuery(
    Guid EventId,
    string Bucket,
    int Page = 1,
    int PageSize = 50) : IRequest<PagedPhotosDto>;

public sealed class ListPhotographerPhotosQueryHandler(
    IPhotoAssetRepository photos,
    ICurrentUserAccessor currentUser,
    IEventStaffAccessQuery staffAccess) : IRequestHandler<ListPhotographerPhotosQuery, PagedPhotosDto>
{
    public async Task<PagedPhotosDto> Handle(
        ListPhotographerPhotosQuery request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        await BulkUploadPhotosCommandHandler.EnsurePhotographerAsync(
            request.EventId, userId, staffAccess, cancellationToken);

        var statuses = request.Bucket.Trim().ToLowerInvariant() switch
        {
            "pending" => new[]
            {
                PhotoStatus.Pending, PhotoStatus.Queued, PhotoStatus.Processing, PhotoStatus.Failed
            },
            "matched" => new[] { PhotoStatus.Matched, PhotoStatus.ManualCorrected },
            "review" => new[] { PhotoStatus.NeedsReview, PhotoStatus.RunnerReported },
            _ => throw new DomainException("Bucket phải là pending|matched|review.")
        };

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        var total = await photos.CountByEventAndStatusesAsync(
            request.EventId, statuses, cancellationToken);
        var items = await photos.ListByEventAndStatusesAsync(
            request.EventId, statuses, skip, pageSize, cancellationToken);

        return new PagedPhotosDto(
            items.Select(PhotoDtoMapper.ToListItem).ToList(),
            page,
            pageSize,
            total);
    }
}
