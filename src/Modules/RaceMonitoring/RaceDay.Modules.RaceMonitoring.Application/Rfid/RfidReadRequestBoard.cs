using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Application.Rfid;

public static class RfidReadStatuses
{
    public const string Pending = "Pending";
    public const string Reading = "Reading";
    public const string Observed = "Observed";
    public const string Ambiguous = "Ambiguous";
    public const string NoTag = "NoTag";
    public const string Confirmed = "Confirmed";
    public const string Cancelled = "Cancelled";
    public const string Expired = "Expired";
}

public static class RfidReaderCommands
{
    public const string Assign = "assign";
    public const string Hold = "hold";
    public const string Checkpoint = "checkpoint";
}

public static class RfidReaderModes
{
    public const string Assign = "assign";
    public const string AwaitCheckpoint = "await-checkpoint";
    public const string CheckpointScan = "checkpoint-scan";
}

public enum RfidConfirmClaim
{
    NewlyConfirmed,
    AlreadyConfirmed
}

public sealed record RfidReadRequestDto(
    Guid RequestId,
    Guid EventId,
    Guid RegistrationId,
    string ReaderId,
    string Status,
    IReadOnlyList<string> TagIds,
    DateTime ExpiresAtUtc,
    Guid CreatedByUserId);

public sealed record RfidGatewayPollDto(
    string Command,
    Guid? RequestId,
    string? Status,
    Guid? EventId,
    Guid? RegistrationId,
    string? ReaderId,
    IReadOnlyList<string> ReleaseTagIds);

public sealed record RfidReaderModeDto(string ReaderId, Guid EventId, string Mode);

/// <summary>
/// Hàng đợi đọc trong bộ nhớ. Một reader chỉ có một yêu cầu chưa kết thúc.
/// Mọi đổi trạng thái đi qua cùng một khóa.
/// </summary>
public sealed class RfidReadRequestBoard
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Request> _byId = [];
    private readonly Dictionary<string, Guid> _activeByReader = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ReaderControl> _control = new(StringComparer.OrdinalIgnoreCase);

    public RfidReadRequestBoard()
        : this(TimeProvider.System)
    {
    }

    public static RfidReadRequestBoard ForClock(TimeProvider time) => new(time);

    private RfidReadRequestBoard(TimeProvider time)
    {
        _time = time;
    }

    public RfidReadRequestDto Create(Guid eventId, Guid registrationId, string readerId, Guid userId)
    {
        lock (_gate)
        {
            var key = ReaderKey(readerId);
            ExpireActive(key);
            if (_activeByReader.ContainsKey(key))
            {
                throw new RfidConflictException("Đầu đọc đang có yêu cầu đọc chưa kết thúc.");
            }

            var request = new Request
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                RegistrationId = registrationId,
                ReaderId = key,
                Status = RfidReadStatuses.Pending,
                CreatedByUserId = userId,
                ExpiresAtUtc = UtcNow().Add(Lifetime)
            };
            _byId[request.Id] = request;
            _activeByReader[key] = request.Id;
            return Snapshot(request);
        }
    }

    public RfidReadRequestDto? Find(Guid requestId)
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(requestId, out var request))
            {
                return null;
            }

            Expire(request);
            return Snapshot(request);
        }
    }

    public string ModeOf(string readerId)
    {
        lock (_gate)
        {
            var key = ReaderKey(readerId);
            ExpireActive(key);
            return ModeUnlocked(key);
        }
    }

    public RfidReadRequestDto Cancel(Guid requestId, Guid userId)
    {
        lock (_gate)
        {
            var request = Require(requestId);
            Expire(request);
            if (request.Status == RfidReadStatuses.Confirmed)
            {
                throw new DomainException("Yêu cầu đã xác nhận, không hủy được.");
            }

            if (request.Status is RfidReadStatuses.Cancelled or RfidReadStatuses.Expired)
            {
                return Snapshot(request);
            }

            if (request.CreatedByUserId != userId)
            {
                throw new DomainException("Chỉ người tạo yêu cầu mới hủy được.");
            }

            Finish(request, RfidReadStatuses.Cancelled);
            return Snapshot(request);
        }
    }

    public RfidReadRequestDto Observe(Guid requestId, string readerId, IReadOnlyList<string> normalizedTagIds)
    {
        lock (_gate)
        {
            var request = Require(requestId);
            var key = ReaderKey(readerId);
            if (!string.Equals(request.ReaderId, key, StringComparison.OrdinalIgnoreCase))
            {
                throw new DomainException("Yêu cầu đọc không thuộc đầu đọc này.");
            }

            Expire(request);
            if (request.Status == RfidReadStatuses.Pending)
            {
                throw new DomainException("Đầu đọc chưa nhận yêu cầu đọc.");
            }

            if (request.Status is not (
                RfidReadStatuses.Reading
                or RfidReadStatuses.Observed
                or RfidReadStatuses.NoTag
                or RfidReadStatuses.Ambiguous))
            {
                throw new DomainException("Yêu cầu đọc không còn nhận kết quả.");
            }

            var unique = new List<string>();
            foreach (var tag in normalizedTagIds)
            {
                if (unique.Contains(tag, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                unique.Add(tag);
            }

            request.TagIds.Clear();
            request.TagIds.AddRange(unique);
            request.Status = unique.Count switch
            {
                0 => RfidReadStatuses.NoTag,
                1 => RfidReadStatuses.Observed,
                _ => RfidReadStatuses.Ambiguous
            };
            return Snapshot(request);
        }
    }

    public RfidGatewayPollDto ClaimNext(string readerId, Guid? requestId)
    {
        lock (_gate)
        {
            var key = ReaderKey(readerId);
            ExpireActive(key);
            if (_activeByReader.TryGetValue(key, out var activeId) && _byId.TryGetValue(activeId, out var active))
            {
                if (active.Status == RfidReadStatuses.Pending)
                {
                    active.Status = RfidReadStatuses.Reading;
                }

                return Poll(RfidReaderCommands.Assign, active, []);
            }

            var control = Control(key);
            if (requestId is Guid requested
                && _byId.TryGetValue(requested, out var previous)
                && string.Equals(previous.ReaderId, key, StringComparison.OrdinalIgnoreCase)
                && control.RequestId is null)
            {
                control.RequestId = previous.Id;
                control.EventId = previous.EventId;
                control.RegistrationId = previous.RegistrationId;
                control.TerminalStatus = previous.Status;
            }

            if (control.AwaitingCheckpoint)
            {
                return new RfidGatewayPollDto(
                    RfidReaderCommands.Hold,
                    control.RequestId,
                    control.TerminalStatus,
                    control.EventId,
                    control.RegistrationId,
                    key,
                    control.ReleaseTagIds.ToArray());
            }

            var release = control.ReleaseTagIds.ToArray();
            control.ReleaseTagIds.Clear();
            return new RfidGatewayPollDto(
                RfidReaderCommands.Checkpoint,
                control.RequestId,
                control.TerminalStatus,
                control.EventId,
                control.RegistrationId,
                key,
                release);
        }
    }

    public void EnsureConfirmable(Guid requestId, string normalizedTagId)
    {
        lock (_gate)
        {
            var request = Require(requestId);
            Expire(request);
            RejectUnlessConfirmable(request, normalizedTagId);
        }
    }

    public RfidConfirmClaim ClaimConfirm(Guid requestId, string normalizedTagId)
    {
        lock (_gate)
        {
            var request = Require(requestId);
            Expire(request);
            if (request.Status == RfidReadStatuses.Confirmed
                && request.TagIds.Count == 1
                && string.Equals(request.TagIds[0], normalizedTagId, StringComparison.OrdinalIgnoreCase))
            {
                return RfidConfirmClaim.AlreadyConfirmed;
            }

            RejectUnlessConfirmable(request, normalizedTagId);
            Finish(request, RfidReadStatuses.Confirmed);
            return RfidConfirmClaim.NewlyConfirmed;
        }
    }

    private static void RejectUnlessConfirmable(Request request, string normalizedTagId)
    {
        if (request.Status == RfidReadStatuses.Confirmed
            && request.TagIds.Count == 1
            && string.Equals(request.TagIds[0], normalizedTagId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (request.Status == RfidReadStatuses.Confirmed)
        {
            throw new DomainException("Yêu cầu đọc đã xác nhận.");
        }

        if (request.Status == RfidReadStatuses.Cancelled)
        {
            throw new DomainException("Yêu cầu đọc đã hủy.");
        }

        if (request.Status == RfidReadStatuses.Expired)
        {
            throw new DomainException("Yêu cầu đọc đã hết hạn.");
        }

        if (request.Status != RfidReadStatuses.Observed
            || request.TagIds.Count != 1
            || !string.Equals(request.TagIds[0], normalizedTagId, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("Chỉ xác nhận khi yêu cầu đọc được đúng một EPC.");
        }
    }

    public void RevertConfirm(Guid requestId)
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(requestId, out var request) || request.Status != RfidReadStatuses.Confirmed)
            {
                return;
            }

            request.Status = RfidReadStatuses.Observed;
            if (!_activeByReader.ContainsKey(request.ReaderId))
            {
                _activeByReader[request.ReaderId] = request.Id;
            }

            var control = Control(request.ReaderId);
            if (control.RequestId == request.Id)
            {
                control.AwaitingCheckpoint = false;
                control.TerminalStatus = null;
            }
        }
    }

    public void StartCheckpointScan(string readerId)
    {
        lock (_gate)
        {
            var key = ReaderKey(readerId);
            Control(key).AwaitingCheckpoint = false;
        }
    }

    private RfidGatewayPollDto Poll(string command, Request request, IReadOnlyList<string> release) =>
        new(
            command,
            request.Id,
            request.Status,
            request.EventId,
            request.RegistrationId,
            request.ReaderId,
            release);

    private void ExpireActive(string readerId)
    {
        if (_activeByReader.TryGetValue(readerId, out var id) && _byId.TryGetValue(id, out var request))
        {
            Expire(request);
        }
    }

    private void Expire(Request request)
    {
        if (!IsActive(request.Status) || UtcNow() < request.ExpiresAtUtc)
        {
            return;
        }

        Finish(request, RfidReadStatuses.Expired);
    }

    private void Finish(Request request, string status)
    {
        request.Status = status;
        if (_activeByReader.TryGetValue(request.ReaderId, out var activeId) && activeId == request.Id)
        {
            _activeByReader.Remove(request.ReaderId);
        }

        var control = Control(request.ReaderId);
        control.RequestId = request.Id;
        control.EventId = request.EventId;
        control.RegistrationId = request.RegistrationId;
        control.TerminalStatus = status;
        foreach (var tag in request.TagIds)
        {
            if (!control.ReleaseTagIds.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                control.ReleaseTagIds.Add(tag);
            }
        }

        control.AwaitingCheckpoint = status == RfidReadStatuses.Confirmed;
    }

    private string ModeUnlocked(string readerId)
    {
        if (_activeByReader.ContainsKey(readerId))
        {
            return RfidReaderModes.Assign;
        }

        return Control(readerId).AwaitingCheckpoint
            ? RfidReaderModes.AwaitCheckpoint
            : RfidReaderModes.CheckpointScan;
    }

    private Request Require(Guid requestId) =>
        _byId.TryGetValue(requestId, out var request)
            ? request
            : throw new DomainException("Không tìm thấy yêu cầu đọc.");

    private ReaderControl Control(string readerId)
    {
        if (!_control.TryGetValue(readerId, out var control))
        {
            control = new ReaderControl();
            _control[readerId] = control;
        }

        return control;
    }

    private static bool IsActive(string status) => status is
        RfidReadStatuses.Pending
        or RfidReadStatuses.Reading
        or RfidReadStatuses.Observed
        or RfidReadStatuses.Ambiguous
        or RfidReadStatuses.NoTag;

    private static string ReaderKey(string? readerId)
    {
        if (string.IsNullOrWhiteSpace(readerId))
        {
            throw new DomainException("Mã đầu đọc không hợp lệ.");
        }

        return readerId.Trim();
    }

    private DateTime UtcNow()
    {
        var now = _time.GetUtcNow().UtcDateTime;
        return now.Kind == DateTimeKind.Utc ? now : DateTime.SpecifyKind(now, DateTimeKind.Utc);
    }

    private static RfidReadRequestDto Snapshot(Request request) =>
        new(
            request.Id,
            request.EventId,
            request.RegistrationId,
            request.ReaderId,
            request.Status,
            request.TagIds.ToArray(),
            request.ExpiresAtUtc,
            request.CreatedByUserId);

    private sealed class Request
    {
        public Guid Id { get; init; }
        public Guid EventId { get; init; }
        public Guid RegistrationId { get; init; }
        public string ReaderId { get; init; } = "";
        public string Status { get; set; } = RfidReadStatuses.Pending;
        public List<string> TagIds { get; } = [];
        public DateTime ExpiresAtUtc { get; init; }
        public Guid CreatedByUserId { get; init; }
    }

    private sealed class ReaderControl
    {
        public bool AwaitingCheckpoint { get; set; }
        public List<string> ReleaseTagIds { get; } = [];
        public Guid? RequestId { get; set; }
        public Guid? EventId { get; set; }
        public Guid? RegistrationId { get; set; }
        public string? TerminalStatus { get; set; }
    }
}
