using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Photo.Application.Abstractions;
using RaceDay.Modules.Photo.Application.Photos;
using RaceDay.Modules.Photo.Domain;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Photo.Application.Disputes;

public sealed record PhotoDisputeDto(
    Guid Id,
    Guid PhotoId,
    Guid EventId,
    Guid ReporterUserId,
    string Reason,
    string Status,
    string? ReviewNote,
    DateTime CreatedAtUtc,
    DateTime? ReviewedAtUtc);

public static class DisputeDtoMapper
{
    public static string ToApi(PhotoDisputeStatus s) => s switch
    {
        PhotoDisputeStatus.Open => "open",
        PhotoDisputeStatus.Approved => "approved",
        PhotoDisputeStatus.Rejected => "rejected",
        _ => s.ToString().ToLowerInvariant()
    };

    public static PhotoDisputeDto ToDto(PhotoDispute d) => new(
        d.Id,
        d.PhotoId,
        d.EventId,
        d.ReporterUserId,
        d.Reason,
        ToApi(d.Status),
        d.ReviewNote,
        d.CreatedAtUtc,
        d.ReviewedAtUtc);
}

public sealed record CreatePhotoDisputeCommand(
    Guid PhotoId,
    string Reason) : IRequest<PhotoDisputeDto>;

public sealed class CreatePhotoDisputeCommandValidator : AbstractValidator<CreatePhotoDisputeCommand>
{
    public CreatePhotoDisputeCommandValidator()
    {
        RuleFor(x => x.PhotoId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class CreatePhotoDisputeCommandHandler(
    IPhotoAssetRepository photos,
    IPhotoDisputeRepository disputes,
    IPhotoUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<CreatePhotoDisputeCommand, PhotoDisputeDto>
{
    public async Task<PhotoDisputeDto> Handle(
        CreatePhotoDisputeCommand request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var photo = await photos.GetByIdWithTagsAsync(request.PhotoId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy ảnh.");

        photo.MarkRunnerReported();
        photos.Update(photo);

        var dispute = PhotoDispute.Open(photo.Id, photo.EventId, userId, request.Reason);
        await disputes.AddAsync(dispute, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DisputeDtoMapper.ToDto(dispute);
    }
}

public sealed record ListOpenEventDisputesQuery(Guid EventId)
    : IRequest<IReadOnlyList<PhotoDisputeDto>>;

public sealed class ListOpenEventDisputesQueryHandler(
    IPhotoDisputeRepository disputes,
    ICurrentUserAccessor currentUser,
    IEventStaffAccessQuery staffAccess) : IRequestHandler<ListOpenEventDisputesQuery, IReadOnlyList<PhotoDisputeDto>>
{
    public async Task<IReadOnlyList<PhotoDisputeDto>> Handle(
        ListOpenEventDisputesQuery request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        await BulkUploadPhotosCommandHandler.EnsurePhotographerAsync(
            request.EventId, userId, staffAccess, cancellationToken);

        var list = await disputes.ListOpenByEventAsync(request.EventId, cancellationToken);
        return list.Select(DisputeDtoMapper.ToDto).ToList();
    }
}

public sealed record ListMyDisputesQuery : IRequest<IReadOnlyList<PhotoDisputeDto>>;

public sealed class ListMyDisputesQueryHandler(
    IPhotoDisputeRepository disputes,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListMyDisputesQuery, IReadOnlyList<PhotoDisputeDto>>
{
    public async Task<IReadOnlyList<PhotoDisputeDto>> Handle(
        ListMyDisputesQuery request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var list = await disputes.ListByReporterAsync(userId, cancellationToken);
        return list.Select(DisputeDtoMapper.ToDto).ToList();
    }
}

public sealed record GetMyDisputeQuery(Guid DisputeId) : IRequest<PhotoDisputeDto>;

public sealed class GetMyDisputeQueryHandler(
    IPhotoDisputeRepository disputes,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetMyDisputeQuery, PhotoDisputeDto>
{
    public async Task<PhotoDisputeDto> Handle(
        GetMyDisputeQuery request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var d = await disputes.GetByIdAsync(request.DisputeId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy khiếu nại.");
        if (d.ReporterUserId != userId)
        {
            throw new DomainException("Khiếu nại không thuộc về bạn.");
        }

        return DisputeDtoMapper.ToDto(d);
    }
}

public sealed record ApproveDisputeCommand(Guid EventId, Guid DisputeId)
    : IRequest<PhotoDisputeDto>;

public sealed class ApproveDisputeCommandHandler(
    IPhotoDisputeRepository disputes,
    IPhotoAssetRepository photos,
    IPhotoUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    IEventStaffAccessQuery staffAccess) : IRequestHandler<ApproveDisputeCommand, PhotoDisputeDto>
{
    public async Task<PhotoDisputeDto> Handle(
        ApproveDisputeCommand request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        await BulkUploadPhotosCommandHandler.EnsurePhotographerAsync(
            request.EventId, userId, staffAccess, cancellationToken);

        var d = await disputes.GetByIdAsync(request.DisputeId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy khiếu nại.");
        if (d.EventId != request.EventId)
        {
            throw new DomainException("Khiếu nại không thuộc giải này.");
        }

        d.Approve(userId);
        disputes.Update(d);

        var photo = await photos.GetByIdWithTagsAsync(d.PhotoId, cancellationToken);
        if (photo is not null)
        {
            // Approve dispute: keep in review queue for bib correction
            if (photo.Status == PhotoStatus.RunnerReported)
            {
                photo.ApplyOcrNeedsReview(
                    photo.Tags.Select(t => (t.BibNormalized, t.Confidence, t.BboxJson)).ToList());
                photos.Update(photo);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DisputeDtoMapper.ToDto(d);
    }
}

public sealed record RejectDisputeCommand(Guid EventId, Guid DisputeId, string Reason)
    : IRequest<PhotoDisputeDto>;

public sealed class RejectDisputeCommandValidator : AbstractValidator<RejectDisputeCommand>
{
    public RejectDisputeCommandValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class RejectDisputeCommandHandler(
    IPhotoDisputeRepository disputes,
    IPhotoAssetRepository photos,
    IPhotoUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    IEventStaffAccessQuery staffAccess) : IRequestHandler<RejectDisputeCommand, PhotoDisputeDto>
{
    public async Task<PhotoDisputeDto> Handle(
        RejectDisputeCommand request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        await BulkUploadPhotosCommandHandler.EnsurePhotographerAsync(
            request.EventId, userId, staffAccess, cancellationToken);

        var d = await disputes.GetByIdAsync(request.DisputeId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy khiếu nại.");
        if (d.EventId != request.EventId)
        {
            throw new DomainException("Khiếu nại không thuộc giải này.");
        }

        d.Reject(userId, request.Reason);
        disputes.Update(d);

        var photo = await photos.GetByIdWithTagsAsync(d.PhotoId, cancellationToken);
        photo?.RestoreAfterDisputeApproved();
        if (photo is not null)
        {
            photos.Update(photo);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DisputeDtoMapper.ToDto(d);
    }
}
