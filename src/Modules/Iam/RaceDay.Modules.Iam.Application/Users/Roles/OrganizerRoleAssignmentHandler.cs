using MediatR;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Application.Users.GetUser;
using RaceDay.Modules.Iam.Application.Users.UpdateUser;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Shared.Application.Messaging;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Iam.Application.Users.Roles;

/// <summary>
/// Handles OrganizerRoleRequiredIntegrationEvent by assigning the Organizer role to the user.
/// 
/// This handler is responsible for:
/// - Listening to role assignment requests from other modules (Event)
/// - Fetching current user profile data
/// - Updating user role to Organizer
/// - Publishing confirmation event
/// 
/// Implements best-effort pattern: failures are logged but don't crash the system
/// since the original business operation (event creation) already succeeded.
/// </summary>
public sealed class OrganizerRoleAssignmentHandler(
    ISender sender,
    ILogger<OrganizerRoleAssignmentHandler> logger) : INotificationHandler<IntegrationEventOccurred>
{
    // Well-known organizer role ID from IamDataSeeder.cs
    private static readonly Guid OrganizerRoleId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public async Task Handle(IntegrationEventOccurred notification, CancellationToken cancellationToken)
    {
        if (notification.Event is not OrganizerRoleRequiredIntegrationEvent organizerRequired)
        {
            return;
        }

        try
        {
            // Step 1: Fetch current user profile
            var userDto = await sender.Send(
                new GetUserByIdQuery(organizerRequired.UserId),
                cancellationToken);

            if (userDto == null)
            {
                logger.LogWarning(
                    "User {UserId} not found when attempting to assign organizer role",
                    organizerRequired.UserId);
                return;
            }

            // Step 2: Check if already has organizer role (idempotent)
            if (userDto.RoleId == OrganizerRoleId)
            {
                logger.LogInformation(
                    "User {UserId} already has organizer role, skipping reassignment",
                    organizerRequired.UserId);
                return;
            }

            // Step 3: Build update command preserving all current user data
            var updateCommand = new UpdateUserCommand(
                UserId: organizerRequired.UserId,
                Username: userDto.Username,
                FullName: userDto.FullName,
                Phone: userDto.Phone,
                DateOfBirth: userDto.DateOfBirth,
                Gender: userDto.Gender,
                RoleId: OrganizerRoleId, // ⭐ Changed to organizer role
                Status: userDto.Status,
                ProfileDescription: userDto.ProfileDescription,
                ContactEmail: null,  // UserDto doesn't expose these fields
                ContactPhone: null); // UserDto doesn't expose these fields

            // Step 4: Send update command (will be handled by UpdateUserCommandHandler)
            await sender.Send(updateCommand, cancellationToken);

            logger.LogInformation(
                "Successfully assigned Organizer role to user {UserId}",
                organizerRequired.UserId);
        }
        catch (DomainException ex)
        {
            logger.LogWarning(
                ex,
                "Domain error while assigning organizer role to user {UserId}: {Message}",
                organizerRequired.UserId,
                ex.Message);
            // Don't rethrow - original event already succeeded
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unexpected error while assigning organizer role to user {UserId}",
                organizerRequired.UserId);
            // Don't rethrow - original event already succeeded
        }
    }
}
