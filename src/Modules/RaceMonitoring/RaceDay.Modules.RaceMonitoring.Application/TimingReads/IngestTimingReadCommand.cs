using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.RaceMonitoring.Application.CheckpointScans;
using RaceDay.Modules.RaceMonitoring.Domain;

namespace RaceDay.Modules.RaceMonitoring.Application.TimingReads;

public sealed record TimingReadRequest(
    Guid ReadId,
    string? ReaderId,
    string? TagScheme,
    string? TagId,
    DateTime ObservedAtUtc,
    string? TimestampSource);

public sealed record IngestTimingReadCommand(TimingReadRequest Body) : IRequest<IngestTimingReadResult>;

public sealed record IngestTimingReadResult(
    string Outcome,
    Guid ReadId,
    Guid ScanId,
    Guid? RaceEventLogId,
    long? Sequence,
    Guid EventId,
    Guid CheckpointId,
    Guid RegistrationId);

public sealed class IngestTimingReadCommandValidator : AbstractValidator<IngestTimingReadCommand>
{
    public IngestTimingReadCommandValidator()
    {
        RuleFor(x => x.Body.ReadId).NotEmpty().WithMessage("Mã lượt đọc không hợp lệ.");
        RuleFor(x => x.Body.ReaderId).NotEmpty().WithMessage("Mã đầu đọc không hợp lệ.");
        RuleFor(x => x.Body.TagScheme).NotEmpty().WithMessage("Hệ thẻ không hợp lệ.");
        RuleFor(x => x.Body.TagId).NotEmpty().WithMessage("Mã thẻ không hợp lệ.");
        RuleFor(x => x.Body.ObservedAtUtc)
            .Must(value => value != default)
            .WithMessage("Thời điểm quan sát không hợp lệ.");
        RuleFor(x => x.Body.TimestampSource).NotEmpty().WithMessage("Nguồn mốc thời gian không hợp lệ.");
    }
}

/// <summary>
/// Nhận TimingRead rồi chỉ gọi CheckpointScanRecorder.
/// Start/Finish và passage chính thức của module Event không đi qua lệnh này.
/// </summary>
public sealed class IngestTimingReadCommandHandler(
    ITimingReadResolver resolver,
    ICheckpointScanRecorder recorder,
    ILogger<IngestTimingReadCommandHandler> logger) : IRequestHandler<IngestTimingReadCommand, IngestTimingReadResult>
{
    public async Task<IngestTimingReadResult> Handle(
        IngestTimingReadCommand request,
        CancellationToken cancellationToken)
    {
        var body = request.Body;
        var assignment = await resolver.ResolveAsync(
            body.ReaderId!,
            body.TagScheme!,
            body.TagId!,
            cancellationToken);
        logger.LogInformation(
            "TimingRead {ReadId} reader {ReaderId} {TagScheme}:{TagId} timestampSource {TimestampSource}.",
            body.ReadId,
            body.ReaderId,
            body.TagScheme,
            body.TagId,
            body.TimestampSource);

        var recorded = await recorder.RecordAsync(
            new CheckpointScanRecord(
                body.ReadId,
                assignment.EventId,
                assignment.RegistrationId,
                assignment.CheckpointId,
                ScanMethod.RFID,
                body.ObservedAtUtc),
            cancellationToken);

        return new IngestTimingReadResult(
            recorded.Outcome,
            body.ReadId,
            recorded.ScanId,
            recorded.RaceEventLogId,
            recorded.Sequence,
            recorded.EventId,
            assignment.CheckpointId,
            assignment.RegistrationId);
    }
}
