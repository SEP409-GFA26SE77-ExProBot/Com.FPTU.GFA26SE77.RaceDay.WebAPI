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
    /// <summary>Song song PutObject R2 trong 1 request (giới hạn tránh bão gateway/R2).</summary>
    private const int UploadParallelism = 6;

    public async Task<BulkUploadResultDto> Handle(
        BulkUploadPhotosCommand request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        await EnsurePhotographerAsync(request.EventId, userId, staffAccess, cancellationToken);

        // 1) Đọc + SHA tuần tự (multipart stream), lọc duplicate.
        var pendingUploads = new List<(byte[] Bytes, string FileName, string ContentType, string Sha)>();
        var skipped = 0;
        var seenSha = new HashSet<string>(StringComparer.Ordinal);

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

            if (!seenSha.Add(sha) || await photos.ExistsShaAsync(request.EventId, sha, cancellationToken))
            {
                skipped++;
                continue;
            }

            var contentType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType;
            pendingUploads.Add((bytes, file.FileName, contentType, sha));
        }

        // 2) Upload R2 song song (DbContext chỉ Add sau — không share EF giữa task).
        var gate = new SemaphoreSlim(UploadParallelism);
        var uploadTasks = pendingUploads.Select(async item =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                await using var stream = new MemoryStream(item.Bytes, writable: false);
                var stored = await fileStorage.UploadAsync(
                    stream,
                    item.FileName,
                    item.ContentType,
                    $"event-photos/{request.EventId:N}",
                    cancellationToken);

                return PhotoAsset.CreatePending(
                    request.EventId,
                    userId,
                    stored.FileUrl,
                    item.Sha,
                    item.FileName,
                    item.ContentType,
                    stored.SizeBytes > 0 ? stored.SizeBytes : item.Bytes.LongLength,
                    request.Zone);
            }
            finally
            {
                gate.Release();
            }
        });

        var assets = await Task.WhenAll(uploadTasks);
        foreach (var asset in assets)
        {
            await photos.AddAsync(asset, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new BulkUploadResultDto(
            assets.Length,
            skipped,
            assets.Select(a => a.Id).ToList());
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

public sealed record PublishPhotosCommand(
    Guid EventId,
    IReadOnlyList<Guid>? PhotoIds = null,
    bool AllReady = false) : IRequest<PublishPhotosResultDto>;

public sealed class PublishPhotosCommandValidator : AbstractValidator<PublishPhotosCommand>
{
    public PublishPhotosCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x)
            .Must(x => x.AllReady || (x.PhotoIds is { Count: > 0 }))
            .WithMessage("Chọn photoIds hoặc allReady=true.");
        RuleFor(x => x.PhotoIds!)
            .Must(ids => ids.Count <= 500)
            .When(x => x.PhotoIds is not null)
            .WithMessage("Tối đa 500 ảnh / lần publish.");
    }
}

public sealed class PublishPhotosCommandHandler(
    IPhotoAssetRepository photos,
    IPhotoUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    IEventStaffAccessQuery staffAccess) : IRequestHandler<PublishPhotosCommand, PublishPhotosResultDto>
{
    public async Task<PublishPhotosResultDto> Handle(
        PublishPhotosCommand request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        await BulkUploadPhotosCommandHandler.EnsurePhotographerAsync(
            request.EventId, userId, staffAccess, cancellationToken);

        IReadOnlyList<PhotoAsset> targets;
        if (request.AllReady)
        {
            // Cap một batch — FE có thể gọi lại đến hết.
            targets = await photos.ListByEventAndStatusesAsync(
                request.EventId,
                [PhotoStatus.Matched, PhotoStatus.ManualCorrected],
                skip: 0,
                take: 500,
                cancellationToken,
                isPublished: false);
        }
        else
        {
            var ids = request.PhotoIds!.Distinct().ToList();
            var loaded = new List<PhotoAsset>();
            foreach (var id in ids)
            {
                var photo = await photos.GetByIdWithTagsAsync(id, cancellationToken);
                if (photo is null || photo.EventId != request.EventId)
                {
                    throw new DomainException($"Ảnh {id} không thuộc giải này.");
                }

                loaded.Add(photo);
            }

            targets = loaded;
        }

        var count = 0;
        foreach (var photo in targets)
        {
            if (!photo.CanPublish)
            {
                continue;
            }

            photo.Publish();
            photos.Update(photo);
            count++;
        }

        if (count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new PublishPhotosResultDto(count);
    }
}
