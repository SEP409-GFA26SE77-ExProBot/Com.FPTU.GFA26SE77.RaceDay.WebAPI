using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Domain;

namespace RaceDay.Modules.RaceMonitoring.Application.RaceEventLogs;

/// <summary>Các trường hiển thị đọc từ metadata JSON của log. Log cũ chỉ có scanMethod.</summary>
public sealed record RaceEventLogMetadata(string? ScanMethod, string? CheckpointName, string? RunnerName)
{
    public static readonly RaceEventLogMetadata Empty = new(null, null, null);
}

public static class RaceEventLogMapper
{
    private const string ScanMethodKey = "scanMethod";
    private const string CheckpointNameKey = "checkpointName";
    private const string RunnerNameKey = "runnerName";

    // Giữ nguyên chữ tiếng Việt thay vì \uXXXX để metadata không chạm giới hạn độ dài.
    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public static RaceEventLogNotification ToNotification(RaceEventLog log)
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
            metadata.RunnerName);
    }

    /// <summary>Metadata của một lượt qua checkpoint. Bỏ khóa rỗng để JSON gọn.</summary>
    public static string SerializeCheckpointMetadata(string scanMethod, string? checkpointName, string? runnerName)
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

        return JsonSerializer.Serialize(values, MetadataJsonOptions);
    }

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
                ReadString(root, RunnerNameKey));
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
