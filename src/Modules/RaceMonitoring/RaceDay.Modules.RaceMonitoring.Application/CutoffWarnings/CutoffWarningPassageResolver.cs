using Microsoft.Extensions.Logging;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.RunnerMonitoring;

namespace RaceDay.Modules.RaceMonitoring.Application.CutoffWarnings;

/// <summary>
/// Closes a runner's active warnings as soon as a scan is recorded, instead of waiting for the next monitor tick.
/// </summary>
public interface ICutoffWarningPassageResolver
{
    Task ResolveAsync(Guid registrationId, Guid checkpointId, Guid scanId, CancellationToken cancellationToken);
}

public sealed class CutoffWarningPassageResolver(
    ICutoffWarningRepository warnings,
    IEventCutoffScheduleQuery schedule,
    IRaceMonitoringUnitOfWork unitOfWork,
    ICutoffWarningPublisher publisher,
    TimeProvider clock,
    ILogger<CutoffWarningPassageResolver> logger) : ICutoffWarningPassageResolver
{
    public async Task ResolveAsync(Guid registrationId, Guid checkpointId, Guid scanId, CancellationToken cancellationToken)
    {
        var active = await warnings.ListActiveByRegistrationAsync(registrationId, cancellationToken);
        if (active.Count == 0)
        {
            return;
        }

        var scannedOrder = await schedule.GetCheckpointSortOrderAsync(checkpointId, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var resolved = new List<CutoffWarning>();
        foreach (var warning in active)
        {
            CutoffWarningResolution? reason = warning.CheckpointId == checkpointId
                ? CutoffWarningResolution.PassedCheckpoint
                : scannedOrder > warning.CheckpointSortOrder
                    ? CutoffWarningResolution.PassedLaterCheckpoint
                    : null;
            if (reason is { } resolution && warning.Resolve(resolution, now, scanId))
            {
                resolved.Add(warning);
            }
        }

        if (resolved.Count == 0)
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (var warning in resolved)
        {
            try
            {
                publisher.Publish(CutoffWarningMapper.ToNotification(warning, CutoffWarningNotificationKind.Resolved, now));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Không phát được thông báo cut-off {WarningId}.", warning.Id);
            }
        }
    }
}
