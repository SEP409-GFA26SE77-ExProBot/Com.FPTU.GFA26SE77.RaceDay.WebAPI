using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Domain.RunnerMonitoring;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Application.CutoffWarnings;

public sealed record DismissCutoffWarningRequest(string? Note);

/// <summary>Removes the runner from the active Warning list. Keeps the record, registration and scans.</summary>
public sealed record DismissCutoffWarningCommand(Guid WarningId, DismissCutoffWarningRequest? Body)
    : IRequest<CutoffWarningDto>;

public sealed class DismissCutoffWarningCommandValidator : AbstractValidator<DismissCutoffWarningCommand>
{
    public DismissCutoffWarningCommandValidator()
    {
        RuleFor(x => x.WarningId).NotEmpty().WithMessage("Tham chiếu cảnh báo không hợp lệ.");
        RuleFor(x => x.Body!.Note)
            .MaximumLength(CutoffWarningFieldLengths.DismissalNote)
            .WithMessage("Ghi chú vượt quá độ dài cho phép.")
            .When(x => x.Body is not null);
    }
}

public sealed class DismissCutoffWarningCommandHandler(
    ICutoffWarningRepository warnings,
    IRaceMonitoringUnitOfWork unitOfWork,
    ICutoffWarningPublisher publisher,
    CutoffWarningAccess access,
    TimeProvider clock,
    ILogger<DismissCutoffWarningCommandHandler> logger)
    : IRequestHandler<DismissCutoffWarningCommand, CutoffWarningDto>
{
    public async Task<CutoffWarningDto> Handle(DismissCutoffWarningCommand request, CancellationToken cancellationToken)
    {
        var viewer = access.CurrentViewer();
        var warning = await warnings.GetByIdAsync(request.WarningId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy cảnh báo.");
        await access.EnsureCanManageAsync(warning.EventId, viewer, cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        warning.Dismiss(viewer.UserId, request.Body?.Note, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            publisher.Publish(CutoffWarningMapper.ToNotification(warning, CutoffWarningNotificationKind.Dismissed, now));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không phát được thông báo cut-off {WarningId}.", warning.Id);
        }

        return CutoffWarningMapper.ToDto(warning);
    }
}
