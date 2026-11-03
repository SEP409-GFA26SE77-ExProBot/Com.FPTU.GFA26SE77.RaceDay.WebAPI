using FluentValidation;
using MediatR;
using RaceDay.Modules.Device.Contracts.Sos;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Application.EmergencyResponses;

public sealed record CancelEmergencyResponseRequest(string? Reason);

public sealed record CancelEmergencyResponseCommand(Guid Id, CancelEmergencyResponseRequest Body)
    : IRequest<EmergencyResponseDto>;

public sealed class CancelEmergencyResponseCommandValidator : AbstractValidator<CancelEmergencyResponseCommand>
{
    public CancelEmergencyResponseCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Tham chiếu phản hồi khẩn cấp không hợp lệ.");
        RuleFor(x => x.Body).NotNull().WithMessage("Lý do hủy phản hồi không được để trống.");
        When(x => x.Body is not null, () =>
        {
            RuleFor(x => x.Body.Reason)
                .NotEmpty()
                .WithMessage("Lý do hủy phản hồi không được để trống.");
        });
    }
}

public sealed class CancelEmergencyResponseCommandHandler(
    IEmergencyResponseRepository responses,
    IEmergencyAssignmentRepository assignments,
    IRaceMonitoringUnitOfWork unitOfWork,
    ISosAlertScopeQuery scopes,
    IEventOrganizerAccessQuery access,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser) : IRequestHandler<CancelEmergencyResponseCommand, EmergencyResponseDto>
{
    public async Task<EmergencyResponseDto> Handle(
        CancelEmergencyResponseCommand request,
        CancellationToken cancellationToken)
    {
        var response = await responses.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new DomainException("Không tìm thấy phản hồi khẩn cấp.");
        var scope = await EmergencyResponseAccess.EnsureCanCoordinateResponseAsync(
            response, scopes, access, sosOps, currentUser, cancellationToken);
        response.Cancel(request.Body.Reason, DateTime.UtcNow);
        var rows = await assignments.ListTrackedByResponseAsync(response.Id, cancellationToken);
        var completedAt = response.CancelledAtUtc
            ?? throw new DomainException("Thời điểm hủy phản hồi không hợp lệ.");
        foreach (var row in rows)
        {
            row.CompleteBecauseResponseCancelled(completedAt);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return EmergencyResponseMapper.ToDto(response, scope);
    }
}
