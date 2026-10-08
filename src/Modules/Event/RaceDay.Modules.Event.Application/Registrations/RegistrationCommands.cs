using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Application.Validation;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Registrations;

public sealed record RegistrationDto(
    Guid Id,
    Guid EventId,
    Guid DistanceId,
    Guid UserId,
    string Status,
    string FullName,
    string Email,
    string Phone,
    DateOnly? DateOfBirth,
    string? Gender,
    string? IdNumber,
    int? HeightCm,
    int? WeightKg,
    string? BloodType,
    string? EmergencyName,
    string? EmergencyPhone,
    string? ShirtType,
    string? ShirtSize,
    string? ShirtColor,
    JsonElement? MedicalCodes,
    string? HealthCertFileName,
    string? HealthCertUrl,
    string? BibNumber,
    string? RejectionReason,
    string? TicketCode,
    object? QrPayload,
    string? EventName,
    string? DistanceLabel,
    double? DistanceKm,
    decimal? Price,
    string? Currency,
    DateTime CreatedAtUtc);

public static class RegistrationDtoMapper
{
    public static string ToApiStatus(RegistrationStatus status) => status switch
    {
        RegistrationStatus.PendingPayment => "pending_payment",
        RegistrationStatus.PendingApproval => "pending_approval",
        RegistrationStatus.Confirmed => "confirmed",
        RegistrationStatus.Rejected => "rejected",
        RegistrationStatus.Cancelled => "cancelled",
        _ => status.ToString().ToLowerInvariant()
    };

    public static RegistrationDto ToDto(
        EventRegistration r,
        RaceEvent? race = null,
        EventDistance? distance = null)
    {
        JsonElement? medical = null;
        if (!string.IsNullOrWhiteSpace(r.MedicalCodesJson) && r.MedicalCodesJson is not "[]")
        {
            using var doc = JsonDocument.Parse(r.MedicalCodesJson);
            medical = doc.RootElement.Clone();
        }

        string? ticketCode = null;
        object? qr = null;
        if (!string.IsNullOrWhiteSpace(r.BibNumber) && r.Status == RegistrationStatus.Confirmed)
        {
            ticketCode = $"RD-{r.EventId:N}-{r.BibNumber}-{r.UserId:N}"[..Math.Min(80, $"RD-{r.EventId:N}-{r.BibNumber}-{r.UserId:N}".Length)];
            qr = new
            {
                user_id = r.UserId,
                bib = r.BibNumber,
                event_id = r.EventId,
                distance = distance?.DistanceKm ?? 0
            };
        }

        return new RegistrationDto(
            r.Id, r.EventId, r.DistanceId, r.UserId, ToApiStatus(r.Status),
            r.FullName, r.Email, r.Phone, r.DateOfBirth, r.Gender, r.IdNumber,
            r.HeightCm, r.WeightKg, r.BloodType, r.EmergencyName, r.EmergencyPhone,
            r.ShirtType, r.ShirtSize, r.ShirtColor, medical, r.HealthCertFileName, r.HealthCertUrl,
            r.BibNumber, r.RejectionReason, ticketCode, qr,
            race?.Name,
            distance is null ? null : $"{distance.Type} · {distance.Name}",
            distance?.DistanceKm,
            distance?.Price,
            distance?.Currency,
            r.CreatedAtUtc);
    }
}

public sealed record CreateRegistrationCommand(
    Guid EventId,
    Guid DistanceId,
    string FullName,
    string Email,
    string Phone,
    DateOnly? DateOfBirth,
    string? Gender,
    string? IdNumber,
    int? HeightCm,
    int? WeightKg,
    string? BloodType,
    string? EmergencyName,
    string? EmergencyPhone,
    string? ShirtType,
    string? ShirtSize,
    string? ShirtColor,
    IReadOnlyList<string>? MedicalCodes,
    string? HealthCertFileName,
    string? HealthCertUrl,
    bool LiabilityAccepted) : IRequest<RegistrationDto>;

public sealed class CreateRegistrationCommandValidator : AbstractValidator<CreateRegistrationCommand>
{
    public CreateRegistrationCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.DistanceId).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(100);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(20).VietnamPhone();
        RuleFor(x => x.DateOfBirth).DateOfBirth();
        RuleFor(x => x.Gender).MaximumLength(20).OptionalGenderLabel();
        RuleFor(x => x.IdNumber).MaximumLength(20).OptionalIdNumber();
        RuleFor(x => x.HeightCm).OptionalHeightCm();
        RuleFor(x => x.WeightKg).OptionalWeightKg();
        RuleFor(x => x.BloodType).MaximumLength(5).OptionalBloodType();
        RuleFor(x => x.EmergencyName).MaximumLength(100);
        RuleFor(x => x.EmergencyPhone).MaximumLength(20).OptionalVietnamPhone();
        RuleFor(x => x.ShirtType).MaximumLength(50);
        RuleFor(x => x.ShirtSize).MaximumLength(20);
        RuleFor(x => x.ShirtColor).MaximumLength(50);
        RuleFor(x => x.HealthCertFileName).MaximumLength(200);
        RuleFor(x => x.HealthCertUrl).OptionalHttpUrl();
        RuleFor(x => x.LiabilityAccepted).Equal(true)
            .WithMessage("Cần chấp nhận điều khoản trách nhiệm.");
    }
}

public sealed class CreateRegistrationCommandHandler(
    IRaceEventRepository events,
    IEventRegistrationRepository registrations,
    ICurrentUserAccessor currentUser,
    IEventUnitOfWork unitOfWork,
    IEventBus eventBus) : IRequestHandler<CreateRegistrationCommand, RegistrationDto>
{
    public async Task<RegistrationDto> Handle(CreateRegistrationCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập để đăng ký giải.");
        var race = await events.GetByIdWithDetailsAsync(request.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        race.EnsureAcceptsRegistration();
        var distance = race.GetDistance(request.DistanceId);
        if (!distance.HasAvailableSlot)
        {
            throw new DomainException("Cự ly đã hết slot.");
        }

        var active = await registrations.ListActiveByUserEventAsync(request.EventId, userId, cancellationToken);
        var sameDistance = active.FirstOrDefault(x => x.DistanceId == request.DistanceId);
        if (sameDistance is not null)
        {
            // Idempotent: submit lại cùng cự ly khi còn chờ TT → trả đơn cũ (FE thanh toán tiếp).
            if (sameDistance.Status == RegistrationStatus.PendingPayment)
            {
                return RegistrationDtoMapper.ToDto(sameDistance, race, distance);
            }

            throw new DomainException("Bạn đã đăng ký cự ly này.");
        }

        var blocking = active.FirstOrDefault(x =>
            x.Status is RegistrationStatus.PendingApproval or RegistrationStatus.Confirmed);
        if (blocking is not null)
        {
            throw new DomainException(
                "Bạn đã có đăng ký đang chờ duyệt/đã xác nhận cho giải này. Không thể đăng ký thêm cự ly khác.");
        }

        // Một giải / một đơn chờ TT: đổi cự ly → hủy các pending_payment cũ.
        foreach (var unpaid in active.Where(x => x.Status == RegistrationStatus.PendingPayment))
        {
            unpaid.CancelUnpaid();
            registrations.Update(unpaid);
        }

        var medicalJson = JsonSerializer.Serialize(request.MedicalCodes ?? Array.Empty<string>());
        var reg = EventRegistration.Create(
            request.EventId,
            request.DistanceId,
            userId,
            request.FullName,
            request.Email,
            request.Phone,
            request.DateOfBirth,
            request.Gender,
            request.IdNumber,
            request.HeightCm,
            request.WeightKg,
            request.BloodType,
            request.EmergencyName,
            request.EmergencyPhone,
            request.ShirtType,
            request.ShirtSize,
            request.ShirtColor,
            medicalJson,
            request.HealthCertFileName,
            request.HealthCertUrl,
            request.LiabilityAccepted);

        // Status = PendingPayment — FE gọi Payment checkout; slot tăng sau khi paid.
        await registrations.AddAsync(reg, cancellationToken);
        await eventBus.PublishAsync(
            new RegistrationCreatedIntegrationEvent(reg.Id, reg.EventId, reg.UserId),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RegistrationDtoMapper.ToDto(reg, race, distance);
    }
}

public sealed record ListMyRegistrationsQuery : IRequest<IReadOnlyList<RegistrationDto>>;

public sealed class ListMyRegistrationsQueryHandler(
    IRaceEventRepository events,
    IEventRegistrationRepository registrations,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListMyRegistrationsQuery, IReadOnlyList<RegistrationDto>>
{
    public async Task<IReadOnlyList<RegistrationDto>> Handle(ListMyRegistrationsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var items = await registrations.ListByUserAsync(userId, cancellationToken);
        var result = new List<RegistrationDto>();
        foreach (var r in items)
        {
            var race = await events.GetByIdWithDetailsAsync(r.EventId, cancellationToken);
            EventDistance? distance = null;
            try
            {
                distance = race?.GetDistance(r.DistanceId);
            }
            catch
            {
                // ignore missing distance
            }

            result.Add(RegistrationDtoMapper.ToDto(r, race, distance));
        }

        return result;
    }
}

public sealed record GetMyRegistrationByIdQuery(Guid RegistrationId) : IRequest<RegistrationDto>;

public sealed class GetMyRegistrationByIdQueryHandler(
    IRaceEventRepository events,
    IEventRegistrationRepository registrations,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetMyRegistrationByIdQuery, RegistrationDto>
{
    public async Task<RegistrationDto> Handle(GetMyRegistrationByIdQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var reg = await registrations.GetByIdAsync(request.RegistrationId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy đăng ký.");

        if (reg.UserId != userId && !currentUser.IsAdmin)
        {
            throw new DomainException("Không có quyền xem đăng ký này.");
        }

        var race = await events.GetByIdWithDetailsAsync(reg.EventId, cancellationToken);
        EventDistance? distance = null;
        try
        {
            distance = race?.GetDistance(reg.DistanceId);
        }
        catch
        {
            // ignore
        }

        return RegistrationDtoMapper.ToDto(reg, race, distance);
    }
}

public sealed record ListEventRegistrationsQuery(Guid EventId) : IRequest<IReadOnlyList<RegistrationDto>>;

public sealed class ListEventRegistrationsQueryHandler(
    IRaceEventRepository events,
    IEventRegistrationRepository registrations,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListEventRegistrationsQuery, IReadOnlyList<RegistrationDto>>
{
    public async Task<IReadOnlyList<RegistrationDto>> Handle(ListEventRegistrationsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var race = await events.GetByIdWithDetailsAsync(request.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        if (!currentUser.IsAdmin && race.OrganizerUserId != userId)
        {
            throw new DomainException("Bạn không phải chủ giải này.");
        }

        var items = await registrations.ListByEventAsync(request.EventId, cancellationToken);
        return items.Select(r =>
        {
            EventDistance? d = null;
            try { d = race.GetDistance(r.DistanceId); } catch { /* ignore */ }
            return RegistrationDtoMapper.ToDto(r, race, d);
        }).ToList();
    }
}

public sealed record ApproveRegistrationCommand(Guid RegistrationId, string? BibNumber) : IRequest<RegistrationDto>;

public sealed class ApproveRegistrationCommandHandler(
    IRaceEventRepository events,
    IEventRegistrationRepository registrations,
    ICurrentUserAccessor currentUser,
    IEventUnitOfWork unitOfWork,
    IEventBus eventBus,
    IEmailSender emailSender,
    ILogger<ApproveRegistrationCommandHandler> logger) : IRequestHandler<ApproveRegistrationCommand, RegistrationDto>
{
    public async Task<RegistrationDto> Handle(ApproveRegistrationCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var reg = await registrations.GetByIdAsync(request.RegistrationId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy đăng ký.");
        var race = await events.GetByIdWithDetailsAsync(reg.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        if (!currentUser.IsAdmin && race.OrganizerUserId != userId)
        {
            throw new DomainException("Bạn không phải chủ giải này.");
        }

        var bib = request.BibNumber;
        if (string.IsNullOrWhiteSpace(bib))
        {
            bib = await registrations.NextBibNumberAsync(reg.EventId, cancellationToken);
        }

        reg.Confirm(bib);
        registrations.Update(reg);

        EventDistance? distance = null;
        try { distance = race.GetDistance(reg.DistanceId); } catch { /* ignore */ }

        var ticketCode = RegistrationEmailTemplates.BuildTicketCode(reg);
        var qrJson = RegistrationEmailTemplates.BuildQrJson(reg, distance);

        await eventBus.PublishAsync(
            new RegistrationConfirmedIntegrationEvent(reg.Id, reg.EventId, reg.UserId, reg.BibNumber!, ticketCode),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            if (distance is null)
            {
                throw new DomainException("Không tìm thấy cự ly của đăng ký.");
            }

            await emailSender.SendAsync(
                RegistrationEmailTemplates.TicketConfirmed(reg, race, distance, ticketCode, qrJson),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send ticket email for registration {RegistrationId}", reg.Id);
        }

        return RegistrationDtoMapper.ToDto(reg, race, distance);
    }
}

public sealed record RejectRegistrationCommand(Guid RegistrationId, string Reason) : IRequest<RegistrationDto>;

public sealed class RejectRegistrationCommandValidator : AbstractValidator<RejectRegistrationCommand>
{
    public RejectRegistrationCommandValidator()
        => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
}

public sealed class RejectRegistrationCommandHandler(
    IRaceEventRepository events,
    IEventRegistrationRepository registrations,
    ICurrentUserAccessor currentUser,
    IEventUnitOfWork unitOfWork,
    IEventBus eventBus,
    IEmailSender emailSender,
    ILogger<RejectRegistrationCommandHandler> logger) : IRequestHandler<RejectRegistrationCommand, RegistrationDto>
{
    public async Task<RegistrationDto> Handle(RejectRegistrationCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var reg = await registrations.GetByIdAsync(request.RegistrationId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy đăng ký.");
        var race = await events.GetByIdWithDetailsAsync(reg.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        if (!currentUser.IsAdmin && race.OrganizerUserId != userId)
        {
            throw new DomainException("Bạn không phải chủ giải này.");
        }

        reg.Reject(request.Reason);
        registrations.Update(reg);
        await eventBus.PublishAsync(
            new RegistrationRejectedIntegrationEvent(reg.Id, reg.EventId, reg.UserId, request.Reason),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await emailSender.SendAsync(
                RegistrationEmailTemplates.RegistrationRejected(reg, race, request.Reason),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send rejection email for registration {RegistrationId}", reg.Id);
        }

        EventDistance? distance = null;
        try { distance = race.GetDistance(reg.DistanceId); } catch { /* ignore */ }
        return RegistrationDtoMapper.ToDto(reg, race, distance);
    }
}

public sealed record CancelUnpaidRegistrationCommand(Guid RegistrationId) : IRequest<RegistrationDto>;

public sealed class CancelUnpaidRegistrationCommandHandler(
    IRaceEventRepository events,
    IEventRegistrationRepository registrations,
    ICurrentUserAccessor currentUser,
    IEventUnitOfWork unitOfWork) : IRequestHandler<CancelUnpaidRegistrationCommand, RegistrationDto>
{
    public async Task<RegistrationDto> Handle(CancelUnpaidRegistrationCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var reg = await registrations.GetByIdAsync(request.RegistrationId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy đăng ký.");

        if (reg.UserId != userId && !currentUser.IsAdmin)
        {
            throw new DomainException("Không thể hủy đăng ký của người khác.");
        }

        reg.CancelUnpaid();
        registrations.Update(reg);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var race = await events.GetByIdWithDetailsAsync(reg.EventId, cancellationToken);
        EventDistance? distance = null;
        if (race is not null)
        {
            try { distance = race.GetDistance(reg.DistanceId); } catch { /* ignore */ }
        }

        return RegistrationDtoMapper.ToDto(reg, race, distance);
    }
}
