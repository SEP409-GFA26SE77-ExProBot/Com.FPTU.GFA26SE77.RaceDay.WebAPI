using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Application.CheckpointScans;

public sealed record RecordCheckpointScanRequest(
    Guid ScanId,
    Guid RegistrationId,
    Guid CheckpointId,
    string? ScanMethod,
    DateTime ScannedAtUtc);

public sealed record RecordCheckpointScanCommand(Guid EventId, RecordCheckpointScanRequest Body)
    : IRequest<RecordCheckpointScanResult>;

public sealed class RecordCheckpointScanCommandValidator : AbstractValidator<RecordCheckpointScanCommand>
{
    public RecordCheckpointScanCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty().WithMessage("Tham chiếu giải không hợp lệ.");
        RuleFor(x => x.Body.ScanId).NotEmpty().WithMessage("Mã lượt quét không hợp lệ.");
        RuleFor(x => x.Body.RegistrationId).NotEmpty().WithMessage("Tham chiếu đăng ký không hợp lệ.");
        RuleFor(x => x.Body.CheckpointId).NotEmpty().WithMessage("Tham chiếu checkpoint không hợp lệ.");
        RuleFor(x => x.Body.ScanMethod).NotEmpty().WithMessage("Phương thức quét không hợp lệ.");
        RuleFor(x => x.Body.ScannedAtUtc).Must(value => value != default).WithMessage("Thời điểm quét checkpoint không hợp lệ.");
    }
}

public sealed class RecordCheckpointScanCommandHandler(
    ICheckpointScanRecorder recorder,
    IEventOrganizerAccessQuery access,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser) : IRequestHandler<RecordCheckpointScanCommand, RecordCheckpointScanResult>
{
    public async Task<RecordCheckpointScanResult> Handle(
        RecordCheckpointScanCommand request,
        CancellationToken cancellationToken)
    {
        await RaceTimelineAccess.EnsureCanRecordAsync(
            request.EventId, access, sosOps, currentUser, cancellationToken);

        if (!TryParseScanMethod(request.Body.ScanMethod, out var scanMethod))
        {
            throw new DomainException("Phương thức quét không hợp lệ.");
        }

        return await recorder.RecordAsync(
            new CheckpointScanRecord(
                request.Body.ScanId,
                request.EventId,
                request.Body.RegistrationId,
                request.Body.CheckpointId,
                scanMethod,
                request.Body.ScannedAtUtc),
            cancellationToken);
    }

    internal static bool TryParseScanMethod(string? raw, out ScanMethod scanMethod)
    {
        scanMethod = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (!Enum.GetNames<ScanMethod>().Any(name => name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        scanMethod = Enum.Parse<ScanMethod>(trimmed, ignoreCase: true);
        return true;
    }
}
