namespace RaceDay.RfidGateway.Timing;

public sealed record RfidObservedTag(string TagId, DateTime ObservedAtUtc);

public sealed record RfidCommandPoll(
    string Command,
    Guid? RequestId,
    string? Status,
    Guid? EventId,
    Guid? RegistrationId,
    string? ReaderId,
    IReadOnlyList<string>? ReleaseTagIds);

public interface IRfidCommandClient
{
    Task<RfidCommandPoll> PollAsync(string readerId, Guid? requestId, CancellationToken cancellationToken);

    Task PostObservationsAsync(
        Guid requestId,
        string readerId,
        IReadOnlyList<RfidObservedTag> tags,
        CancellationToken cancellationToken);
}
