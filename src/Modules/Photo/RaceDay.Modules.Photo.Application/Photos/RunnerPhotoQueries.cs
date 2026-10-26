using MediatR;
using RaceDay.Modules.Photo.Application.Abstractions;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Photo.Application.Photos;

public sealed record SearchPhotosQuery(
    Guid EventId,
    string Bib,
    int Page = 1,
    int PageSize = 50) : IRequest<PagedPhotosDto>;

public sealed class SearchPhotosQueryHandler(
    IPhotoAssetRepository photos,
    ICurrentUserAccessor currentUser) : IRequestHandler<SearchPhotosQuery, PagedPhotosDto>
{
    public async Task<PagedPhotosDto> Handle(
        SearchPhotosQuery request,
        CancellationToken cancellationToken)
    {
        _ = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var bib = request.Bib.Trim().ToUpperInvariant();
        if (bib.Length == 0)
        {
            throw new DomainException("Thiếu số BIB.");
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        var total = await photos.CountSearchByEventAndBibAsync(
            request.EventId, bib, cancellationToken);
        var items = await photos.SearchByEventAndBibAsync(
            request.EventId, bib, skip, pageSize, cancellationToken);

        return new PagedPhotosDto(
            items.Select(PhotoDtoMapper.ToListItem).ToList(),
            page,
            pageSize,
            total);
    }
}

public sealed record GetPhotoDetailQuery(Guid PhotoId) : IRequest<PhotoDetailDto>;

public sealed class GetPhotoDetailQueryHandler(
    IPhotoAssetRepository photos,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetPhotoDetailQuery, PhotoDetailDto>
{
    public async Task<PhotoDetailDto> Handle(
        GetPhotoDetailQuery request,
        CancellationToken cancellationToken)
    {
        _ = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var photo = await photos.GetByIdWithTagsAsync(request.PhotoId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy ảnh.");

        if (!photo.IsSearchable && photo.Status != Domain.PhotoStatus.RunnerReported)
        {
            throw new DomainException("Ảnh chưa sẵn sàng để xem.");
        }

        return PhotoDtoMapper.ToDetail(photo);
    }
}
