using MediatR;
using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Shared.Application.Messaging;

/// <summary>
/// In-process bridge so modules (e.g. AuditLog) can react to integration events
/// without referencing another module's Application layer.
/// </summary>
public sealed record IntegrationEventOccurred(IIntegrationEvent Event) : INotification;
