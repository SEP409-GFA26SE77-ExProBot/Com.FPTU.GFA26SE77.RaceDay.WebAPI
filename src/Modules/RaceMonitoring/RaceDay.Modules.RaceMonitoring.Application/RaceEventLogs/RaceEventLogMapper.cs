using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Domain;

namespace RaceDay.Modules.RaceMonitoring.Application.RaceEventLogs;

/// <summary>Các trường hiển thị đọc từ metadata JSON của log. Log cũ chỉ có scanMethod.</summary>
public sealed record RaceEventLogMetadata(
    string? ScanMethod,
    string? CheckpointName,
    string? RunnerName,
    Guid? SimulationRunId = null)
{
    public static readonly RaceEventLogMetadata Empty = new(null, null, null);
}

public static class RaceEventLogMapper
{
    private const string ScanMethodKey = "scanMethod";
    private const string CheckpointNameKey = "checkpointName";
    private const string RunnerNameKey = "runnerName";
    public const string SimulationRunIdKey = "simulationRunId";

    // Giữ nguyên chữ tiếng Việt thay vì \uXXXX để metadata không chạm giới hạn độ dài.
    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    /// <param name="chipStartAtUtc">The runner's chip start, when the caller resolved it.</param>
    public static RaceEventLogNotification ToNotification(RaceEventLog log, DateTime? chipStartAtUtc = null)
    {
        var metadata = ReadMetadata(log.Metadata);
        return new RaceEventLogNotification(
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
            metadata.ScanMethod,
            metadata.CheckpointName,
            metadata.RunnerName,
            chipStartAtUtc,
            log.SourceType == RaceEventLogSource.Simulation,
            metadata.SimulationRunId);
    }

    /// <summary>Metadata của một lượt qua checkpoint. Bỏ khóa rỗng để JSON gọn.</summary>
    public static string SerializeCheckpointMetadata(
        string scanMethod,
        string? checkpointName,
        string? runnerName,
        Guid? simulationRunId = null)
    {
        var values = new Dictionary<string, string> { [ScanMethodKey] = scanMethod };
        if (!string.IsNullOrWhiteSpace(checkpointName))
        {
            values[CheckpointNameKey] = checkpointName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(runnerName))
        {
            values[RunnerNameKey] = runnerName.Trim();
        }

        if (simulationRunId is Guid runId)
        {
            values[SimulationRunIdKey] = runId.ToString("D");
        }

        return JsonSerializer.Serialize(values, MetadataJsonOptions);
    }

    /// <summary>JSON fragment for a jsonb containment (@>) match on the simulation run id.</summary>
    public static string SimulationRunIdContainment(Guid runId)
        => JsonSerializer.Serialize(
            new Dictionary<string, string> { [SimulationRunIdKey] = runId.ToString("D") },
            MetadataJsonOptions);

    public static string? ReadScanMethod(string? metadata) => ReadMetadata(metadata).ScanMethod;

    public static RaceEventLogMetadata ReadMetadata(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return RaceEventLogMetadata.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(metadata);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return RaceEventLogMetadata.Empty;
            }

            return new RaceEventLogMetadata(
                ReadString(root, ScanMethodKey),
                ReadString(root, CheckpointNameKey),
                ReadString(root, RunnerNameKey),
                Guid.TryParse(ReadString(root, SimulationRunIdKey), out var runId) ? runId : null);
        }
        catch (JsonException)
        {
            return RaceEventLogMetadata.Empty;
        }
    }

    private static string? ReadString(JsonElement root, string key)
        => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
