using System.Threading.Channels;

namespace RaceDay.Modules.RaceMonitoring.Application.Abstractions;

public interface ICutoffWarningPublisher
{
    void Publish(CutoffWarningNotification notification);
}

/// <summary>In-process fan-out to SSE listeners. Each listener filters by the events its user may manage.</summary>
public interface ICutoffWarningBroker : ICutoffWarningPublisher
{
    ICutoffWarningSubscription Subscribe();
}

public interface ICutoffWarningSubscription : IDisposable
{
    ChannelReader<CutoffWarningNotification> Reader { get; }
}

public static class CutoffWarningNotificationKind
{
    public const string Raised = "CutoffWarningRaised";
    public const string Resolved = "CutoffWarningResolved";
    public const string Dismissed = "CutoffWarningDismissed";
}

public sealed record CutoffWarningNotification(
    string Kind,
    Guid WarningId,
    Guid EventId,
    string EventName,
    Guid RegistrationId,
    string BibNumber,
    string RunnerName,
    Guid CheckpointId,
    string CheckpointName,
    DateTime CutoffAtUtc,
    string Status,
    DateTime OccurredAtUtc);
