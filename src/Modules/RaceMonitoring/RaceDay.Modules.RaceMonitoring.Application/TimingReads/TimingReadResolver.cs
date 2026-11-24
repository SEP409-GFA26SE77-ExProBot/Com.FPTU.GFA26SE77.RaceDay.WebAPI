using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Application.TimingReads;

public interface ITimingReadResolver
{
    Task<TimingAssignment> ResolveAsync(
        string readerId,
        string tagScheme,
        string tagId,
        CancellationToken cancellationToken = default);
}

public sealed record TimingAssignment(Guid EventId, Guid CheckpointId, Guid RegistrationId);

/// <summary>
/// Reader lấy từ cấu hình. EPC có dòng DB trong đúng giải thì dùng dòng đó ngay sau khi lưu;
/// không có thì dùng RfidGateway:Tags của Phase 1.
/// </summary>
public sealed class TimingReadResolver(
    RfidGatewayOptions options,
    IRfidTagAssignmentRepository assignments) : ITimingReadResolver
{
    public async Task<TimingAssignment> ResolveAsync(
        string readerId,
        string tagScheme,
        string tagId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(readerId))
        {
            throw new DomainException("Mã đầu đọc không hợp lệ.");
        }

        var readerKey = readerId.Trim();
        var readers = options.Readers
            .Where(reader => string.Equals(reader.ReaderId?.Trim(), readerKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (readers.Count != 1 || readers[0].EventId == Guid.Empty || readers[0].CheckpointId == Guid.Empty)
        {
            throw new DomainException(
                readers.Count > 1
                    ? "Đầu đọc được gán nhiều hơn một checkpoint."
                    : "Đầu đọc chưa được gán checkpoint.");
        }

        var reader = readers[0];
        var normalizedTag = TimingTagIdentity.Normalize(tagScheme, tagId);
        var stored = await assignments.FindByTagAsync(
            reader.EventId,
            RfidTagAssignment.EpcScheme,
            normalizedTag,
            cancellationToken);
        if (stored is not null)
        {
            return new TimingAssignment(reader.EventId, reader.CheckpointId, stored.RegistrationId);
        }

        var scheme = tagScheme.Trim();
        var tags = options.Tags
            .Where(tag => SameTag(tag, reader.EventId, scheme, normalizedTag))
            .ToList();
        if (tags.Count != 1 || tags[0].RegistrationId == Guid.Empty)
        {
            throw new DomainException(
                tags.Count > 1
                    ? "Thẻ được gán nhiều hơn một đăng ký trong phạm vi giải này."
                    : "Thẻ chưa được gán cho vận động viên trong phạm vi giải này.");
        }

        return new TimingAssignment(reader.EventId, reader.CheckpointId, tags[0].RegistrationId);
    }

    private static bool SameTag(RfidTagBinding tag, Guid eventId, string scheme, string normalizedTag)
    {
        if (tag.EventId != eventId
            || string.IsNullOrWhiteSpace(tag.TagScheme)
            || string.IsNullOrWhiteSpace(tag.TagId)
            || !tag.TagScheme.Trim().Equals(scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            return TimingTagIdentity.Normalize(tag.TagScheme, tag.TagId) == normalizedTag;
        }
        catch (DomainException)
        {
            return false;
        }
    }
}

public static class TimingTagIdentity
{
    public static string Normalize(string? tagScheme, string? tagId)
    {
        if (string.IsNullOrWhiteSpace(tagScheme))
        {
            throw new DomainException("Hệ thẻ không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(tagId))
        {
            throw new DomainException("Mã thẻ không hợp lệ.");
        }

        var scheme = tagScheme.Trim();
        var value = tagId.Trim().Replace(" ", "", StringComparison.Ordinal).Replace(":", "", StringComparison.Ordinal);
        if (!scheme.Equals("EPC", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var hex = value.ToUpperInvariant();
        if (hex.Length < 4 || hex.Length % 2 != 0 || hex.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new DomainException("EPC không hợp lệ.");
        }

        return hex;
    }
}
