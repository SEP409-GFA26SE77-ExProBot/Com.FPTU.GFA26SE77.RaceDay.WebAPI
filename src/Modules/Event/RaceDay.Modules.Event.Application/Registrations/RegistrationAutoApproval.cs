using Microsoft.Extensions.Logging;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.Event.Application.Registrations;

/// <summary>
/// VĐV tick «không bệnh lý» (hoặc để trống medical) → auto Confirm + cấp BIB sau thanh toán / resubmit.
/// Có bệnh lý đặc biệt → giữ PendingApproval để BTC duyệt tay.
/// </summary>
internal static class RegistrationAutoApproval
{
    public static bool IsEligible(EventRegistration reg) => !reg.HasSpecialMedicalConditions();

    public static async Task<bool> TryConfirmAsync(
        EventRegistration reg,
        RaceEvent race,
        EventDistance distance,
        IEventRegistrationRepository registrations,
        IEventBus eventBus,
        IEmailSender emailSender,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!IsEligible(reg))
        {
            return false;
        }

        if (reg.Status != RegistrationStatus.PendingApproval)
        {
            return false;
        }

        if (!distance.HasBibRange)
        {
            logger.LogWarning(
                "Auto-approve skipped for registration {RegistrationId}: distance {DistanceId} has no BIB range.",
                reg.Id,
                distance.Id);
            return false;
        }

        var bib = await registrations.NextBibNumberAsync(reg.EventId, reg.DistanceId, cancellationToken);
        reg.Confirm(bib);
        registrations.Update(reg);

        var ticketCode = RegistrationEmailTemplates.BuildTicketCode(reg);
        var qrJson = RegistrationEmailTemplates.BuildQrJson(reg, distance);

        await eventBus.PublishAsync(
            new RegistrationConfirmedIntegrationEvent(reg.Id, reg.EventId, reg.UserId, reg.BibNumber!, ticketCode),
            cancellationToken);

        try
        {
            await emailSender.SendAsync(
                RegistrationEmailTemplates.TicketConfirmed(reg, race, distance, ticketCode, qrJson),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send auto-approve ticket email for registration {RegistrationId}", reg.Id);
        }

        return true;
    }
}
