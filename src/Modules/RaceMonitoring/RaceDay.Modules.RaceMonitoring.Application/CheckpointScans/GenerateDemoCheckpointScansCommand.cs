using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Application.RaceEventLogs;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Application.CheckpointScans;

public sealed record GenerateDemoCheckpointScansRequest(int? Count);

public sealed record GenerateDemoCheckpointScansResult(
    Guid EventId,
    int Requested,
    int Accepted,
    IReadOnlyList<RecordCheckpointScanResult> Items);

public sealed record GenerateDemoCheckpointScansCommand(Guid EventId, int? Count)
    : IRequest<GenerateDemoCheckpointScansResult>;

public sealed class GenerateDemoCheckpointScansCommandValidator : AbstractValidator<GenerateDemoCheckpointScansCommand>
{
    public GenerateDemoCheckpointScansCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty().WithMessage("Tham chiếu giải không hợp lệ.");
        RuleFor(x => x.Count)
            .Must(count => count is null || (count.Value >= 1 && count.Value <= RaceEventLogStreamPolicy.DemoMaxCount))
            .WithMessage($"Số dòng demo phải từ 1 đến {RaceEventLogStreamPolicy.DemoMaxCount}.");
    }
}

/// <summary>
/// Caller demo: rút registration và checkpoint có thật, rồi gọi cùng recorder với API ghi scan.
/// </summary>
public sealed class GenerateDemoCheckpointScansCommandHandler(
    IEventRegistrationRaceQuery registrations,
    IEventCheckpointQuery checkpoints,
    ICheckpointScanRecorder recorder,
    IEventOrganizerAccessQuery access,
    IEventSosOpsAccessQuery sosOps,
    ICurrentUserAccessor currentUser) : IRequestHandler<GenerateDemoCheckpointScansCommand, GenerateDemoCheckpointScansResult>
{
    public async Task<GenerateDemoCheckpointScansResult> Handle(
        GenerateDemoCheckpointScansCommand request,
        CancellationToken cancellationToken)
    {
        await RaceTimelineAccess.EnsureCanRecordAsync(
            request.EventId, access, sosOps, currentUser, cancellationToken);

        var count = request.Count ?? RaceEventLogStreamPolicy.DemoDefaultCount;
        if (count is < 1 or > RaceEventLogStreamPolicy.DemoMaxCount)
        {
            count = RaceEventLogStreamPolicy.DemoDefaultCount;
        }

        var eligibleRegistrations = await registrations.ListEligibleByEventAsync(request.EventId, cancellationToken);
        var courseCheckpoints = await checkpoints.ListByEventAsync(request.EventId, cancellationToken);
        var checkpointsByDistance = courseCheckpoints
            .GroupBy(checkpoint => checkpoint.DistanceId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var pool = eligibleRegistrations
            .Where(registration => checkpointsByDistance.ContainsKey(registration.DistanceId))
            .ToArray();
        if (pool.Length == 0)
        {
            throw new DomainException("Giải chưa có VĐV confirmed có BIB và checkpoint trên đúng cự ly.");
        }

        var methods = Enum.GetValues<ScanMethod>();
        var origin = DateTime.UtcNow.AddSeconds(-count);
        var items = new List<RecordCheckpointScanResult>(count);
        for (var index = 0; index < count; index++)
        {
            var registration = pool[Random.Shared.Next(pool.Length)];
            var distanceCheckpoints = checkpointsByDistance[registration.DistanceId];
            var checkpoint = distanceCheckpoints[Random.Shared.Next(distanceCheckpoints.Length)];
            var scanMethod = methods[Random.Shared.Next(methods.Length)];
            items.Add(await recorder.RecordAsync(
                new CheckpointScanRecord(
                    Guid.NewGuid(),
                    request.EventId,
                    registration.RegistrationId,
                    checkpoint.CheckpointId,
                    scanMethod,
                    origin.AddSeconds(index)),
                cancellationToken));
        }

        return new GenerateDemoCheckpointScansResult(
            request.EventId,
            count,
            items.Count(item => item.Outcome == CheckpointScanRecorder.AcceptedOutcome),
            items);
    }
}
