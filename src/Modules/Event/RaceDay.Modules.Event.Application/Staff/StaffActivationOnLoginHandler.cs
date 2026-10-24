using MediatR;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Shared.Application.Messaging;

namespace RaceDay.Modules.Event.Application.Staff;

/// <summary>
/// Sau đăng nhập thành công: chuyển staff Invited → Active (kích hoạt ≠ đăng ký).
/// </summary>
public sealed class StaffActivationOnLoginHandler(
    IEventStaffMemberRepository staff,
    IEventUnitOfWork unitOfWork,
    ILogger<StaffActivationOnLoginHandler> logger) : INotificationHandler<IntegrationEventOccurred>
{
    public async Task Handle(IntegrationEventOccurred notification, CancellationToken cancellationToken)
    {
        if (notification.Event is not UserLoggedInIntegrationEvent loggedIn)
        {
            return;
        }

        var pending = await staff.ListInvitedByEmailOrUserAsync(
            loggedIn.Email,
            loggedIn.UserId,
            cancellationToken);
        if (pending.Count == 0)
        {
            return;
        }

        foreach (var member in pending)
        {
            member.Activate(loggedIn.UserId);
            staff.Update(member);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Activated {Count} staff invite(s) for user {UserId}",
            pending.Count,
            loggedIn.UserId);
    }
}
