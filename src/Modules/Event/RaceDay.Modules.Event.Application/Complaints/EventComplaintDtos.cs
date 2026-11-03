using System;
using System.Collections.Generic;

namespace RaceDay.Modules.Event.Application.Complaints;

public sealed record EventComplaintDto(
    Guid Id,
    Guid EventId,
    string EventName,
    Guid DistanceId,
    string DistanceName,
    Guid UserId,
    string Title,
    string Content,
    IReadOnlyList<string> EvidenceLinks,
    string Status,
    string ProcessingStatus,
    string? ResponseContent,
    DateTime CreatedAtUtc,
    DateTime? RepliedAtUtc);

public sealed record OrganizerComplaintsPagedDto(
    IReadOnlyList<EventComplaintDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int PendingCount);

public sealed record MyComplaintsPagedDto(
    IReadOnlyList<EventComplaintDto> Items,
    int Page,
    int PageSize,
    int TotalCount);