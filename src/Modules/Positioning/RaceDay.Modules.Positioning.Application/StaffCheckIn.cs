using System.Security.Cryptography;
using System.Text;
using MediatR;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Positioning.Contracts;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Positioning.Application;

/// <summary>
/// Stateless rotating QR codes for staff check-in. A code is an HMAC of (event, 60-second window),
/// so every API instance and every organizer screen shows the same code for the same minute.
/// </summary>
public sealed class StaffCheckInCodes(byte[] key)
{
    public const int LifetimeSeconds = 60;
    /// <summary>A code scanned just after it rotated is still accepted for this long.</summary>
    public const int GraceSeconds = 5;
    private const string Prefix = "raceday-staff-checkin";

    public StaffCheckInCode Issue(Guid eventId, DateTimeOffset now)
    {
        var window = Window(now);
        return new StaffCheckInCode(
            Format(eventId, window),
            DateTimeOffset.FromUnixTimeSeconds(window * LifetimeSeconds),
            DateTimeOffset.FromUnixTimeSeconds((window + 1) * LifetimeSeconds),
            now);
    }

    public bool IsValid(Guid eventId, string? code, DateTimeOffset now)
    {
        var scanned = code?.Trim();
        if (string.IsNullOrEmpty(scanned) || scanned.Length > 200) return false;

        var window = Window(now);
        var inGrace = now.ToUnixTimeSeconds() - window * LifetimeSeconds < GraceSeconds;
        return Matches(scanned, Format(eventId, window))
            || inGrace && Matches(scanned, Format(eventId, window - 1));
    }

    private static long Window(DateTimeOffset now) => now.ToUnixTimeSeconds() / LifetimeSeconds;

    private string Format(Guid eventId, long window)
    {
        var signature = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{eventId:N}:{window}"));
        return $"{Prefix}:{eventId:N}:{window}:{Convert.ToHexStringLower(signature.AsSpan(0, 12))}";
    }

    private static bool Matches(string scanned, string expected)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(scanned), Encoding.UTF8.GetBytes(expected));
}

/// <summary>Who has checked in by QR for an event. Position reports from staff are refused until they do.</summary>
public interface IStaffCheckInStore
{
    Task<DateTimeOffset?> GetAsync(Guid eventId, Guid userId, CancellationToken ct);
    Task SetAsync(Guid eventId, Guid userId, DateTimeOffset checkedInAtUtc, CancellationToken ct);
    Task RemoveAsync(Guid eventId, Guid userId, CancellationToken ct);
}

/// <summary>Organizer only. Returns null when the caller is not the event's organizer.</summary>
public sealed record IssueStaffCheckInCodeQuery(Guid EventId, Guid UserId) : IRequest<StaffCheckInCode?>;

public sealed class IssueStaffCheckInCodeQueryHandler(PositioningAccess access, StaffCheckInCodes codes, TimeProvider clock)
    : IRequestHandler<IssueStaffCheckInCodeQuery, StaffCheckInCode?>
{
    public async Task<StaffCheckInCode?> Handle(IssueStaffCheckInCodeQuery request, CancellationToken cancellationToken)
    {
        var race = await access.GetRaceAsync(request.EventId, cancellationToken);
        if (race is null || race.OrganizerUserId != request.UserId) return null;

        var now = clock.GetUtcNow();
        StaffCheckInRules.EnsureOpen(race, now);
        return codes.Issue(request.EventId, now);
    }
}

/// <summary>Staff or photographer with an active assignment. Returns null when the caller has none.</summary>
public sealed record StaffCheckInCommand(Guid EventId, Guid UserId, string? Code) : IRequest<StaffCheckInReceipt?>;

public sealed class StaffCheckInCommandHandler(
    PositioningAccess access,
    StaffCheckInCodes codes,
    IStaffCheckInStore checkIns,
    TimeProvider clock) : IRequestHandler<StaffCheckInCommand, StaffCheckInReceipt?>
{
    public async Task<StaffCheckInReceipt?> Handle(StaffCheckInCommand request, CancellationToken cancellationToken)
    {
        var reporting = await access.GetReportingAccessAsync(request.EventId, request.UserId, cancellationToken);
        if (reporting is null || reporting.Assignments.Count == 0) return null;
        var race = await access.GetRaceAsync(request.EventId, cancellationToken);
        if (race is null) return null;

        var now = clock.GetUtcNow();
        StaffCheckInRules.EnsureOpen(race, now);
        if (!codes.IsValid(request.EventId, request.Code, now))
        {
            throw new DomainException(StaffCheckInRules.WrongCode);
        }

        await checkIns.SetAsync(request.EventId, request.UserId, now, cancellationToken);
        return new StaffCheckInReceipt(now);
    }
}

public sealed record StaffCheckOutCommand(Guid EventId, Guid UserId) : IRequest;

public sealed class StaffCheckOutCommandHandler(IStaffCheckInStore checkIns) : IRequestHandler<StaffCheckOutCommand>
{
    public Task Handle(StaffCheckOutCommand request, CancellationToken cancellationToken)
        => checkIns.RemoveAsync(request.EventId, request.UserId, cancellationToken);
}

public static class StaffCheckInRules
{
    public const string WrongCode = "Mã QR không đúng hoặc đã hết hạn. Hãy quét mã đang hiển thị trên màn hình của BTC.";
    public const string NotCheckedIn = "Bạn chưa check-in. Bật Check-in và quét mã QR của BTC.";

    public static void EnsureOpen(EventOrganizerAccessInfo race, DateTimeOffset now)
    {
        var opensAt = PositioningAccess.RaceDayOpensAt(race);
        if (opensAt is null)
        {
            throw new DomainException(race.StartsAtUtc is null
                ? "Giải chưa có giờ xuất phát nên chưa mở check-in nhân sự."
                : "Giải đã kết thúc, bị hủy hoặc tạm khóa nên không check-in được.");
        }
        if (now < opensAt)
        {
            throw new DomainException("Check-in nhân sự mở từ 1 giờ trước giờ xuất phát.");
        }
    }
}
