using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Application.Validation;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Events.Update;

public sealed record UpdateDraftRaceEventCommand(
    Guid EventId,
    string? Name,
    string? Tagline,
    string? Location,
    DateOnly? Date,
    TimeOnly? StartTime,
    string? Description,
    string? BannerImage,
    int? TotalRunners,
    OrganizerDto? Organizer,
    object? Rules,
    object? TicketPricing = null,
    object? Rewards = null,
    bool SetRegistrationWindow = false,
    DateTime? RegistrationOpensAtUtc = null,
    DateTime? RegistrationClosesAtUtc = null,
    IReadOnlyList<EventDocumentDto>? Documents = null,
    DateOnly? EndDate = null,
    bool ClearEndDate = false) : IRequest<RaceEventDto>;

public sealed class UpdateDraftRaceEventCommandValidator : AbstractValidator<UpdateDraftRaceEventCommand>
{
    public UpdateDraftRaceEventCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        When(x => x.Name is not null, () => RuleFor(x => x.Name!).NotEmpty().MaximumLength(200));
        When(x => x.Tagline is not null, () => RuleFor(x => x.Tagline!).MaximumLength(300));
        When(x => x.BannerImage is not null, () => RuleFor(x => x.BannerImage!).OptionalHttpUrl());
        When(x => x.Location is not null, () => RuleFor(x => x.Location!).NotEmpty().MaximumLength(300));
        When(x => x.Date.HasValue, () => RuleFor(x => x.Date!.Value).EventDate());
        When(x => x.Description is not null, () => RuleFor(x => x.Description!).MaximumLength(10_000));
        When(x => x.TotalRunners.HasValue, () => RuleFor(x => x.TotalRunners!.Value).InclusiveBetween(0, 500_000));
        When(x => x.Organizer is not null, () =>
        {
            RuleFor(x => x.Organizer!.Name).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Organizer!.Email).NotEmpty().EmailAddress().MaximumLength(100);
            // Cho phép trống khi draft từ gói; nếu có SĐT thì phải đúng format VN.
            RuleFor(x => x.Organizer!.Phone).MaximumLength(30).OptionalVietnamPhone();
        });
        When(x => x.Documents is { Count: > 0 }, () =>
        {
            RuleForEach(x => x.Documents!).ChildRules(doc =>
            {
                doc.RuleFor(d => d.DocumentKey).NotEmpty().MaximumLength(100);
                doc.RuleFor(d => d.Title).NotEmpty().MaximumLength(200);
                doc.RuleFor(d => d.FileName).MaximumLength(200);
                doc.RuleFor(d => d.FileUrl).OptionalHttpUrl();
            });
        });
    }
}

public sealed class UpdateDraftRaceEventCommandHandler(
    IRaceEventRepository events,
    ICurrentUserAccessor currentUser,
    IEventUnitOfWork unitOfWork) : IRequestHandler<UpdateDraftRaceEventCommand, RaceEventDto>
{
    public async Task<RaceEventDto> Handle(UpdateDraftRaceEventCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new DomainException("Cần đăng nhập.");

        var race = await events.GetByIdWithDetailsAsync(request.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        if (!currentUser.IsAdmin && race.OrganizerUserId != userId)
        {
            throw new DomainException("Bạn không phải chủ giải này.");
        }

        var hasScalarPatch =
            request.Name is not null
            || request.Tagline is not null
            || request.Location is not null
            || request.Date.HasValue
            || request.StartTime.HasValue
            || request.EndDate.HasValue
            || request.ClearEndDate
            || request.Description is not null
            || request.BannerImage is not null
            || request.TotalRunners.HasValue
            || request.Organizer is not null
            || request.Rules is not null
            || request.TicketPricing is not null
            || request.Rewards is not null;

        // Documents: ExecuteDelete + AddRange — Clear() navigation gây concurrency (giống cự ly).
        if (request.Documents is not null)
        {
            if (race.Status is not (RaceEventStatus.Draft or RaceEventStatus.Rejected))
            {
                throw new DomainException("Không thể chỉnh sửa giải ở trạng thái hiện tại.");
            }

            var docs = request.Documents.Select(doc =>
                EventDocument.Create(
                    race.Id,
                    doc.DocumentKey,
                    doc.Title,
                    doc.FileName,
                    doc.FileUrl,
                    doc.Required)).ToList();

            await events.PersistDocumentsReplaceAsync(race.Id, docs, cancellationToken);

            if (!hasScalarPatch && !request.SetRegistrationWindow)
            {
                var refreshed = await events.GetByIdWithDetailsAsync(request.EventId, cancellationToken)
                    ?? throw new DomainException("Không tìm thấy giải sau khi lưu tài liệu.");
                return RaceEventDtoMapper.ToDetail(refreshed);
            }

            race = await events.GetByIdWithDetailsAsync(request.EventId, cancellationToken)
                ?? throw new DomainException("Không tìm thấy giải.");
        }

        if (hasScalarPatch)
        {
            OrganizerInfo? organizer = null;
            if (request.Organizer is { } org)
            {
                var bank = org.BeneficiaryAccount;
                organizer = OrganizerInfo.Create(
                    org.Name,
                    org.Email,
                    InputRules.NormalizeOptionalPhoneOrEmpty(org.Phone),
                    org.LegalEntity,
                    org.Website,
                    org.Verified,
                    bank?.AccountHolder,
                    bank?.AccountNumber,
                    bank?.BankName,
                    bank?.Branch,
                    bank?.SwiftCode,
                    bank?.TransferNote);
            }

            race.UpdateDraftProfile(
                request.Name,
                request.Tagline,
                request.Location,
                request.Date,
                request.StartTime,
                request.Description,
                request.BannerImage,
                request.TotalRunners,
                organizer,
                request.Rules is not null ? RaceEventDtoMapper.SerializeOptional(request.Rules) : null,
                request.TicketPricing is not null ? RaceEventDtoMapper.SerializeOptional(request.TicketPricing) : null,
                request.Rewards is not null ? RaceEventDtoMapper.SerializeOptional(request.Rewards) : null,
                request.EndDate,
                request.ClearEndDate);
        }

        if (request.SetRegistrationWindow)
        {
            race.SetRegistrationWindow(request.RegistrationOpensAtUtc, request.RegistrationClosesAtUtc);
        }

        // Entity đã tracked — không gọi Update() (sẽ đánh dấu children mới thành Modified).
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RaceEventDtoMapper.ToDetail(race);
    }
}
