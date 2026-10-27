using System.Text.Json;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Application.RaceEventLogs;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Application.CheckpointScans;

public interface ICheckpointScanRecorder
{
    Task<RecordCheckpointScanResult> RecordAsync(
        CheckpointScanRecord scan,
        CancellationToken cancellationToken);
}

public sealed record CheckpointScanRecord(
    Guid ScanId,
    Guid EventId,
    Guid RegistrationId,
    Guid CheckpointId,
    ScanMethod ScanMethod,
    DateTime ScannedAtUtc);

public sealed record RecordCheckpointScanResult(
    string Outcome,
    Guid ScanId,
    Guid? RaceEventLogId,
    long? Sequence,
    Guid EventId);

public sealed class CheckpointScanRecorder(
    IEventRegistrationRaceQuery registrations,
    IEventCheckpointQuery checkpoints,
    ICheckpointScanRepository scans,
    IRaceEventLogRepository logs,
    IRaceMonitoringUnitOfWork unitOfWork,
    IRaceEventLogPublisher publisher,
    ILogger<CheckpointScanRecorder> logger) : ICheckpointScanRecorder
{
    public const string AcceptedOutcome = "Accepted";
    public const string DuplicateOutcome = "Duplicate";

    public async Task<RecordCheckpointScanResult> RecordAsync(
        CheckpointScanRecord scan,
        CancellationToken cancellationToken)
    {
        if (scan.ScanId == Guid.Empty)
        {
            throw new DomainException("Mã lượt quét không hợp lệ.");
        }

        if (scan.EventId == Guid.Empty)
        {
            throw new DomainException("Tham chiếu giải không hợp lệ.");
        }

        var existing = await scans.GetByIdAsync(scan.ScanId, cancellationToken);
        if (existing is not null)
        {
            return await DuplicateAsync(existing.Id, scan.EventId, cancellationToken);
        }

        var registration = await registrations.GetByIdAsync(scan.RegistrationId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy đăng ký.");
        if (registration.EventId != scan.EventId)
        {
            throw new DomainException("Đăng ký không thuộc giải này.");
        }

        if (registration.Status is not ("Confirmed" or "Finished"))
        {
            throw new DomainException("Chỉ VĐV confirmed hoặc finished mới ghi nhận qua checkpoint.");
        }

        if (string.IsNullOrWhiteSpace(registration.BibNumber))
        {
            throw new DomainException("Đăng ký chưa có số BIB.");
        }

        var checkpoint = await checkpoints.GetAsync(scan.CheckpointId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy checkpoint.");
        if (checkpoint.EventId != scan.EventId)
        {
            throw new DomainException("Checkpoint không thuộc giải này.");
        }

        if (checkpoint.DistanceId != registration.DistanceId)
        {
            throw new DomainException("Checkpoint không thuộc cự ly của VĐV.");
        }

        var occurredAtUtc = AsUtc(scan.ScannedAtUtc);
        var recordedAtUtc = DateTime.UtcNow;
        var raw = CheckpointScan.Create(
            registration.RegistrationId,
            checkpoint.CheckpointId,
            scan.ScanMethod,
            occurredAtUtc,
            scan.ScanId);
        var log = RaceEventLog.RecordCheckpointPassed(
            registration.EventId,
            registration.DistanceId,
            occurredAtUtc,
            recordedAtUtc,
            registration.BibNumber,
            checkpoint.CheckpointId,
            registration.RegistrationId,
            raw.Id,
            JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["scanMethod"] = raw.ScanMethod.ToString()
            }));

        scans.Add(raw);
        logs.Add(log);
        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved == 0)
        {
            return await DuplicateAsync(scan.ScanId, scan.EventId, cancellationToken);
        }

        var notification = RaceEventLogMapper.ToNotification(log);
        try
        {
            publisher.Publish(notification);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không phát được SSE cho log {LogId}.", log.Id);
        }

        logger.LogInformation(
            "Đã ghi checkpoint {ScanId} và log {LogId} cho giải {EventId}.",
            raw.Id,
            log.Id,
            log.EventId);

        return new RecordCheckpointScanResult(
            AcceptedOutcome,
            raw.Id,
            log.Id,
            log.Sequence,
            log.EventId);
    }

    private async Task<RecordCheckpointScanResult> DuplicateAsync(
        Guid scanId,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        var log = await logs.FindBySourceAsync(RaceEventLogSource.CheckpointScan, scanId, cancellationToken);
        return new RecordCheckpointScanResult(
            DuplicateOutcome,
            scanId,
            log?.Id,
            log?.Sequence,
            log?.EventId ?? eventId);
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
