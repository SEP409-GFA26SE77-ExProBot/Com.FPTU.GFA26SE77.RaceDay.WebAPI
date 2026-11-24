using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.RunnerMonitoring;

namespace RaceDay.Modules.RaceMonitoring.Application.CutoffWarnings;

/// <param name="RunnerStatus">"Warning" while the record is active, otherwise "Cleared".</param>
public sealed record CutoffWarningDto(
    Guid Id,
    Guid EventId,
    string EventName,
    Guid DistanceId,
    string DistanceName,
    Guid RegistrationId,
    string BibNumber,
    string RunnerName,
    string RunnerStatus,
    Guid CheckpointId,
    string CheckpointName,
    double CheckpointKm,
    DateTime CutoffAtUtc,
    Guid? LastCheckpointId,
    string? LastCheckpointName,
    DateTime? LastPassedAtUtc,
    DateTime DetectedAtUtc,
    string Status,
    string? Resolution,
    DateTime? ResolvedAtUtc,
    DateTime? DismissedAtUtc,
    string? DismissalNote);

internal static class CutoffWarningMapper
{
    public static CutoffWarningDto ToDto(CutoffWarning warning)
        => new(
            warning.Id,
            warning.EventId,
            warning.EventName,
            warning.DistanceId,
            warning.DistanceName,
            warning.RegistrationId,
            warning.BibNumber,
            warning.RunnerName,
            warning.IsActive ? "Warning" : "Cleared",
            warning.CheckpointId,
            warning.CheckpointName,
            warning.CheckpointKm,
            warning.CutoffAtUtc,
            warning.LastCheckpointId,
            warning.LastCheckpointName,
            warning.LastPassedAtUtc,
            warning.DetectedAtUtc,
            warning.Status.ToString(),
            warning.Resolution?.ToString(),
            warning.ResolvedAtUtc,
            warning.DismissedAtUtc,
            warning.DismissalNote);

    public static CutoffWarningNotification ToNotification(CutoffWarning warning, string kind, DateTime occurredAtUtc)
        => new(
            kind,
            warning.Id,
            warning.EventId,
            warning.EventName,
            warning.RegistrationId,
            warning.BibNumber,
            warning.RunnerName,
            warning.CheckpointId,
            warning.CheckpointName,
            warning.CutoffAtUtc,
            warning.Status.ToString(),
            occurredAtUtc);

    public static CutoffWarningStatus? ParseStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return CutoffWarningStatus.Active;
        }

        var value = raw.Trim();
        if (value.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Enum.TryParse<CutoffWarningStatus>(value, true, out var status) && Enum.IsDefined(status)
            ? status
            : throw new RaceDay.Shared.Domain.Exceptions.DomainException("Trạng thái lọc không hợp lệ.");
    }
}
