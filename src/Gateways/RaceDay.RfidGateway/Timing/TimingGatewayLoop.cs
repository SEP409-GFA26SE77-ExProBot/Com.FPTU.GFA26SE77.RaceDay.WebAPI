namespace RaceDay.RfidGateway.Timing;

public sealed record TimingGatewayRunResult(int Sent, int Suppressed, int Rejected);

public static class TimingGatewayLoop
{
    public static async Task<TimingGatewayRunResult> RunAsync(
        ITimingReadSource source,
        ITimingReadSender sender,
        TimeSpan suppressWindow,
        bool stopOnRejection,
        CancellationToken cancellationToken,
        Action<string>? log = null)
    {
        var write = log ?? Console.WriteLine;
        var lastSent = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        var sent = 0;
        var suppressed = 0;
        var rejected = 0;

        await foreach (var read in source.ReadAsync(cancellationToken))
        {
            if (suppressWindow > TimeSpan.Zero)
            {
                var key = $"{read.ReaderId}|{read.TagScheme}|{read.TagId}";
                if (lastSent.TryGetValue(key, out var seenAt) && read.ObservedAtUtc - seenAt < suppressWindow)
                {
                    suppressed++;
                    write($"[suppress] {read.TagScheme}:{read.TagId} trong {suppressWindow.TotalSeconds:0}s.");
                    continue;
                }

                lastSent[key] = read.ObservedAtUtc;
            }

            try
            {
                var ack = await sender.SendAsync(read, cancellationToken);
                sent++;
                write(
                    $"[{ack.Outcome}] readId={ack.ReadId} checkpoint={ack.CheckpointId} " +
                    $"registration={ack.RegistrationId} log={ack.RaceEventLogId} sequence={ack.Sequence} " +
                    $"timestampSource={read.TimestampSource}");
            }
            catch (TimingReadRejectedException ex)
            {
                rejected++;
                write($"[rejected] readId={read.ReadId} tag={read.TagId} {ex.Message}");
                if (stopOnRejection)
                {
                    break;
                }
            }
        }

        return new TimingGatewayRunResult(sent, suppressed, rejected);
    }
}
