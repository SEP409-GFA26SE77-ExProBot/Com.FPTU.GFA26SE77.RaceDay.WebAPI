using System.Security.Cryptography;
using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Photo.Application.Abstractions;
using RaceDay.Modules.Photo.Domain;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Photo.Application.Photos;

public sealed record BulkUploadPhotoFile(
    Stream Content,
    string FileName,
    string ContentType,
    long Length);

public sealed record BulkUploadPhotosCommand(
    Guid EventId,
    IReadOnlyList<BulkUploadPhotoFile> Files,
    string? Zone = null) : IRequest<BulkUploadResultDto>;

public sealed class BulkUploadPhotosCommandValidator : AbstractValidator<BulkUploadPhotosCommand>
{
    public BulkUploadPhotosCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.Files).NotEmpty().Must(f => f.Count <= 500)
            .WithMessage("Tối đa 500 ảnh / lần upload.");
    }
}

public sealed class BulkUploadPhotosCommandHandler(
    IPhotoAssetRepository photos,
    IPhotoUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    IEventStaffAccessQuery staffAccess,
    IFileStorage fileStorage) : IRequestHandler<BulkUploadPhotosCommand, BulkUploadResultDto>
{
    private const long MaxBytes = 25L * 1024 * 1024;

    public async Task<BulkUploadResultDto> Handle(
        BulkUploadPhotosCommand request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        await EnsurePhotographerAsync(request.EventId, userId, staffAccess, cancellationToken);

        var accepted = new List<Guid>();
        var skipped = 0;

        foreach (var file in request.Files)
        {
            if (file.Length <= 0 || file.Length > MaxBytes)
            {
                throw new DomainException($"File «{file.FileName}» vượt giới hạn 25MB hoặc rỗng.");
            }

            var ct = (file.ContentType ?? string.Empty).ToLowerInvariant();
            if (ct is not ("image/jpeg" or "image/jpg" or "image/png" or "image/webp"))
            {
                throw new DomainException($"Định dạng không hỗ trợ: {file.FileName}");
            }

            await using var buffer = new MemoryStream();
            await file.Content.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            if (await photos.ExistsShaAsync(request.EventId, sha, cancellationToken))
            {
                skipped++;
                continue;
            }

            buffer.Position = 0;
            var contentType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType;
            var stored = await fileStorage.UploadAsync(
                buffer,
                file.FileName,
                contentType,
                $"event-photos/{request.EventId:N}",
                cancellationToken);

            var asset = PhotoAsset.CreatePending(
                request.EventId,
                userId,
                stored.FileUrl,
                sha,
                file.FileName,
                file.ContentType,
                stored.SizeBytes,
                request.Zone);

            await photos.AddAsync(asset, cancellationToken);
            accepted.Add(asset.Id);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new BulkUploadResultDto(accepted.Count, skipped, accepted);
    }

    public static async Task EnsurePhotographerAsync(
        Guid eventId,
        Guid userId,
        IEventStaffAccessQuery staffAccess,
        CancellationToken cancellationToken)
    {
        if (!await staffAccess.IsActivePhotographerAsync(eventId, userId, cancellationToken))
        {
            throw new DomainException(
                "Bạn không phải photographer Active của giải này — không thể thao tác ảnh.");
        }
    }
}

public sealed record ProcessPendingPhotosCommand(Guid EventId) : IRequest<ProcessPhotosResultDto>;

public sealed class ProcessPendingPhotosCommandHandler(
    IPhotoAssetRepository photos,
    IPhotoUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    IEventStaffAccessQuery staffAccess) : IRequestHandler<ProcessPendingPhotosCommand, ProcessPhotosResultDto>
{
    public async Task<ProcessPhotosResultDto> Handle(
        ProcessPendingPhotosCommand request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        await BulkUploadPhotosCommandHandler.EnsurePhotographerAsync(
            request.EventId, userId, staffAccess, cancellationToken);

        var pending = await photos.ListPendingForProcessAsync(request.EventId, 500, cancellationToken);
        foreach (var photo in pending)
        {
            photo.MarkQueued();
            photos.Update(photo);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new ProcessPhotosResultDto(pending.Count);
    }
}

public sealed record CorrectPhotoBibCommand(
    Guid EventId,
    Guid PhotoId,
    string Bib) : IRequest<PhotoDetailDto>;

public sealed class CorrectPhotoBibCommandValidator : AbstractValidator<CorrectPhotoBibCommand>
{
    public CorrectPhotoBibCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.PhotoId).NotEmpty();
        RuleFor(x => x.Bib).NotEmpty().MaximumLength(30);
    }
}

public sealed class CorrectPhotoBibCommandHandler(
    IPhotoAssetRepository photos,
    IPhotoUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    IEventStaffAccessQuery staffAccess,
    IEventBibRosterQuery rosterQuery) : IRequestHandler<CorrectPhotoBibCommand, PhotoDetailDto>
{
    public async Task<PhotoDetailDto> Handle(
        CorrectPhotoBibCommand request,
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

        var bib = request.Bib.Trim().ToUpperInvariant();
        var roster = await rosterQuery.GetConfirmedBibsAsync(request.EventId, cancellationToken);
        if (roster.Bibs.Count > 0
            && !roster.Bibs.Contains(bib, StringComparer.OrdinalIgnoreCase))
        {
            throw new DomainException(
                $"BIB «{bib}» không có trong danh sách VĐV confirmed của giải.");
        }

        photo.CorrectBib(bib);
        photos.Update(photo);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PhotoDtoMapper.ToDetail(photo);
    }
}

public sealed record DiscardPhotoCommand(
    Guid EventId,
    Guid PhotoId,
    string? Reason = null) : IRequest<PhotoDetailDto>;

public sealed class DiscardPhotoCommandValidator : AbstractValidator<DiscardPhotoCommand>
{
    public DiscardPhotoCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.PhotoId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class DiscardPhotoCommandHandler(
    IPhotoAssetRepository photos,
    IPhotoUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    IEventStaffAccessQuery staffAccess) : IRequestHandler<DiscardPhotoCommand, PhotoDetailDto>
{
    public async Task<PhotoDetailDto> Handle(
        DiscardPhotoCommand request,
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

        if (photo.Status is not (
            PhotoStatus.NeedsReview
            or PhotoStatus.RunnerReported
            or PhotoStatus.Failed
            or PhotoStatus.Pending
            or PhotoStatus.Queued))
        {
            throw new DomainException("Chỉ reject ảnh đang review / pending / lỗi OCR.");
        }

        photo.Discard(string.IsNullOrWhiteSpace(request.Reason)
            ? "photographer_reject"
            : request.Reason!);
        photos.Update(photo);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PhotoDtoMapper.ToDetail(photo);
    }
}
