using MediatR;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Device.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;
using RaceDay.Shared.Application.Messaging;

namespace RaceDay.Modules.RaceMonitoring.Application.EmergencyResponses;

/// <summary>
/// Một SOS nguồn mở ra đúng một hồ sơ Open, loại Medical, ưu tiên Medium.
/// Không acknowledge hay resolve SOS thiết bị. SOS trùng thì bỏ qua.
/// </summary>
public sealed class SosOpenedEmergencyResponseHandler(
    IEmergencyResponseRepository responses,
    IRaceMonitoringUnitOfWork unitOfWork,
    ILogger<SosOpenedEmergencyResponseHandler> logger) : INotificationHandler<IntegrationEventOccurred>
{
    public async Task Handle(IntegrationEventOccurred notification, CancellationToken cancellationToken)
    {
        if (notification.Event is not SosOpenedIntegrationEvent opened)
        {
            return;
        }

        if (opened.SosAlertId == Guid.Empty)
        {
            logger.LogWarning("Bỏ qua SOS mở vì thiếu mã SOS.");
            return;
        }

        if (await responses.ExistsBySourceSosAlertIdAsync(opened.SosAlertId, cancellationToken))
        {
            return;
        }

        var response = EmergencyResponse.Create(
            opened.SosAlertId,
            EmergencyIncidentType.Medical,
            EmergencyPriority.Medium,
            EmergencyResponseStatus.Open);
        responses.Add(response);
        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved == 0)
        {
            return;
        }

        logger.LogInformation(
            "Đã mở hồ sơ ứng cứu {ResponseId} cho SOS {SosAlertId}.",
            response.Id,
            opened.SosAlertId);
    }
}
