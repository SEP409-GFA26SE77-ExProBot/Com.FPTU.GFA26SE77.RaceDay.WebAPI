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
            // Thanh tiến trình: chỉ còn đang chờ/ chạy — Failed không tính "còn pending" (tránh treo UI).
            "queue" => new[]
            {
                PhotoStatus.Pending, PhotoStatus.Queued, PhotoStatus.Processing
            },
            "matched" => new[] { PhotoStatus.Matched, PhotoStatus.ManualCorrected },
            "review" => new[] { PhotoStatus.NeedsReview, PhotoStatus.RunnerReported },
            "discarded" => new[] { PhotoStatus.Discarded },
            _ => throw new DomainException("Bucket phải là pending|queue|matched|review|discarded.")
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
