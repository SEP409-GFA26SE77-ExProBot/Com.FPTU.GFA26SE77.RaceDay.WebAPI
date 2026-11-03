using System.Text.Json;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Domain;

namespace RaceDay.Modules.RaceMonitoring.Application.RaceEventLogs;

public static class RaceEventLogMapper
{
    public static RaceEventLogNotification ToNotification(RaceEventLog log) => new(
        log.Id,
        log.Sequence,
        log.EventId,
        log.EventDistanceId,
        log.EventType.ToString(),
        log.OccurredAtUtc,
        log.RecordedAtUtc,
        log.BibNumber,
        log.CheckpointId,
        log.RegistrationId,
        log.SourceType,
        log.SourceId,
        ReadScanMethod(log.Metadata));

    public static string? ReadScanMethod(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadata);
            return document.RootElement.TryGetProperty("scanMethod", out var value)
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
