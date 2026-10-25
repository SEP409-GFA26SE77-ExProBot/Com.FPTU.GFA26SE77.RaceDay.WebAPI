using System;
using System.Collections.Generic;
using System.Text;

namespace RaceDay.Modules.Event.Application.Complaints;

public sealed record EventComplaintDto(
    Guid Id,
    Guid EventId,
    string EventName,
    Guid DistanceId,
    string DistanceName,
    Guid UserId,
    string Content,
    string Status,
    string? ResponseContent,
    DateTime CreatedAtUtc,
    DateTime? RepliedAtUtc);

public sealed record OrganizerEventComplaintsDto(
    Guid EventId,
    string EventName,
    IReadOnlyList<EventComplaintDto> Complaints);