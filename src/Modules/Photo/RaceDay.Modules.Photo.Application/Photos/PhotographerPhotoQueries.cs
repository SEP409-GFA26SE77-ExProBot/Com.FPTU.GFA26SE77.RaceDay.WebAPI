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

        var bucket = request.Bucket.Trim().ToLowerInvariant();
        PhotoStatus[] statuses;
        bool? publishedFilter = null;
        switch (bucket)
        {
            case "pending":
                statuses =
                [
                    PhotoStatus.Pending, PhotoStatus.Queued, PhotoStatus.Processing, PhotoStatus.Failed
                ];
                break;
            case "queue":
                statuses =
                [
                    PhotoStatus.Pending, PhotoStatus.Queued, PhotoStatus.Processing
                ];
                break;
            case "matched":
                statuses = [PhotoStatus.Matched, PhotoStatus.ManualCorrected];
                break;
            case "ready":
                // Đã khớp OCR/sửa tay nhưng chưa public cho runner.
                statuses = [PhotoStatus.Matched, PhotoStatus.ManualCorrected];
                publishedFilter = false;
                break;
            case "published":
                statuses = [PhotoStatus.Matched, PhotoStatus.ManualCorrected];
                publishedFilter = true;
                break;
            case "review":
                statuses = [PhotoStatus.NeedsReview, PhotoStatus.RunnerReported];
                break;
            case "discarded":
                statuses = [PhotoStatus.Discarded];
                break;
            default:
                throw new DomainException(
                    "Bucket phải là pending|queue|matched|ready|published|review|discarded.");
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        var total = await photos.CountByEventAndStatusesAsync(
            request.EventId, statuses, cancellationToken, publishedFilter);
        var items = await photos.ListByEventAndStatusesAsync(
            request.EventId, statuses, skip, pageSize, cancellationToken, publishedFilter);

        return new PagedPhotosDto(
            items.Select(PhotoDtoMapper.ToListItem).ToList(),
            page,
            pageSize,
            total);
    }
}

public sealed record GetPhotographerPhotoDetailQuery(
    Guid EventId,
    Guid PhotoId) : IRequest<PhotoDetailDto>;

public sealed class GetPhotographerPhotoDetailQueryHandler(
    IPhotoAssetRepository photos,
    ICurrentUserAccessor currentUser,
    IEventStaffAccessQuery staffAccess) : IRequestHandler<GetPhotographerPhotoDetailQuery, PhotoDetailDto>
{
    public async Task<PhotoDetailDto> Handle(
        GetPhotographerPhotoDetailQuery request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        await BulkUploadPhotosCommandHandler.EnsurePhotographerAsync(
            request.EventId, userId, staffAccess, cancellationToken);

        var photo = await photos.GetByIdWithTagsAsync(request.PhotoId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy ảnh.");
        if (photo.EventId != request.EventId)
        {
            throw new DomainException("Ảnh không thuộc giải này.");
        }

        return PhotoDtoMapper.ToDetail(photo);
    }
}
