using RaceDay.RfidGateway.Timing;

namespace RaceDay.RfidGateway.Ypd202;

public sealed record ReaderControlRunResult(int TimingSent, int TimingSuppressed, int AssignmentPolls, int HeldPolls);

/// <summary>
/// Vòng YPD-202 kéo lệnh. Lượt gán không gửi timing-reads và không ghi cửa sổ lọc.
/// Sau khi xác nhận, giữ đến lệnh checkpoint; lượt quét đầu xóa EPC của lượt gán khỏi cửa sổ 15 giây.
/// </summary>
public static class Ypd202ReaderControlLoop
{
    public static async Task<ReaderControlRunResult> RunAsync(
        Func<CancellationToken, Task<IYpd202Session>> connect,
        IRfidCommandClient commands,
        ITimingReadSender timing,
        string readerId,
        TimeSpan suppressWindow,
        TimeSpan pollInterval,
        int? maxPolls,
        CancellationToken cancellationToken,
        Action<string>? log = null)
    {
        var write = log ?? Console.WriteLine;
        var lastSent = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        var sent = 0;
        var suppressed = 0;
        var assignments = 0;
        var held = 0;
        IYpd202Session? session = null;
        Guid? activeRequestId = null;
        var previousCommand = "";

        try
        {
            var polls = 0;
            while (!cancellationToken.IsCancellationRequested && (maxPolls is null || polls < maxPolls))
            {
                RfidCommandPoll poll;
                try
                {
                    poll = await commands.PollAsync(readerId, activeRequestId, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    write($"Không kéo được lệnh đọc: {ex.Message}");
                    await Task.Delay(pollInterval, cancellationToken);
                    continue;
                }

                if (poll.RequestId is Guid requestId)
                {
                    activeRequestId = requestId;
                }

                var command = poll.Command?.Trim().ToLowerInvariant() ?? "";
                if (command == "hold")
                {
                    held++;
                    Clear(lastSent, readerId, poll.ReleaseTagIds);
                    write("[hold] Đã gán thẻ. Chờ lệnh bắt đầu quét checkpoint.");
                    previousCommand = command;
                    polls++;
                    if (maxPolls is null || polls < maxPolls)
                    {
                        await Task.Delay(pollInterval, cancellationToken);
                    }

                    continue;
                }

                var outcome = await InventoryAsync(session, connect, cancellationToken);
                session = outcome.Session;
                if (!outcome.Success)
                {
                    if (maxPolls is not null)
                    {
                        throw outcome.Error ?? new IOException("Không đọc được đầu đọc YPD-202.");
                    }

                    write($"Mất kết nối đầu đọc: {outcome.Error?.Message}. Thử lại sau 2 giây.");
                    session = null;
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                polls++;
                var observations = Unique(outcome.Result!);
                if (command == "assign")
                {
                    assignments++;
                    if (poll.RequestId is not Guid assignmentId)
                    {
                        write("[assign] Thiếu requestId, bỏ qua nhịp gán.");
                    }
                    else
                    {
                        try
                        {
                            await commands.PostObservationsAsync(
                                assignmentId,
                                readerId,
                                observations.Select(item => new RfidObservedTag(item.Epc, item.ObservedAtUtc)).ToArray(),
                                cancellationToken);
                            write(observations.Count == 0
                                ? $"[assign] request={assignmentId} không thấy EPC."
                                : $"[assign] request={assignmentId} {observations.Count} EPC.");
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            write($"[assign] Không nộp được EPC: {ex.Message}");
                        }
                    }

                    previousCommand = command;
                }
                else
                {
                    if (previousCommand == "hold")
                    {
                        lastSent.Clear();
                    }

                    Clear(lastSent, readerId, poll.ReleaseTagIds);
                    foreach (var observation in observations)
                    {
                        var key = SuppressKey(readerId, observation.Epc);
                        if (suppressWindow > TimeSpan.Zero
                            && lastSent.TryGetValue(key, out var seenAt)
                            && observation.ObservedAtUtc - seenAt < suppressWindow)
                        {
                            suppressed++;
                            write($"[suppress] EPC:{observation.Epc} trong {suppressWindow.TotalSeconds:0}s.");
                            continue;
                        }

                        if (suppressWindow > TimeSpan.Zero)
                        {
                            lastSent[key] = observation.ObservedAtUtc;
                        }

                        var read = new TimingRead(
                            Guid.NewGuid(),
                            readerId,
                            "EPC",
                            observation.Epc,
                            observation.ObservedAtUtc,
                            TimestampSources.GatewayClock);
                        try
                        {
                            var ack = await timing.SendAsync(read, cancellationToken);
                            sent++;
                            write(
                                $"[{ack.Outcome}] readId={ack.ReadId} checkpoint={ack.CheckpointId} " +
                                $"registration={ack.RegistrationId} log={ack.RaceEventLogId} sequence={ack.Sequence} " +
                                $"timestampSource={read.TimestampSource}");
                        }
                        catch (TimingReadRejectedException ex)
                        {
                            write($"[rejected] readId={read.ReadId} tag={read.TagId} {ex.Message}");
                        }
                    }

                    if (observations.Count == 0)
                    {
                        write($"[checkpoint] không thấy EPC.");
                    }

                    previousCommand = "checkpoint";
                }

                if (maxPolls is null || polls < maxPolls)
                {
                    await Task.Delay(pollInterval, cancellationToken);
                }
            }
        }
        finally
        {
            session?.Dispose();
        }

        return new ReaderControlRunResult(sent, suppressed, assignments, held);
    }

    private static List<(string Epc, DateTime ObservedAtUtc)> Unique(Ypd202InventoryResult result)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var observations = new List<(string Epc, DateTime ObservedAtUtc)>();
        foreach (var observation in result.Observations)
        {
            if (!seen.Add(observation.Epc))
            {
                continue;
            }

            var observedAtUtc = observation.ObservedAt.UtcDateTime;
            if (observedAtUtc.Kind != DateTimeKind.Utc)
            {
                observedAtUtc = DateTime.SpecifyKind(observedAtUtc, DateTimeKind.Utc);
            }

            observations.Add((observation.Epc, observedAtUtc));
        }

        return observations;
    }

    private static void Clear(
        Dictionary<string, DateTime> lastSent,
        string readerId,
        IReadOnlyList<string>? tags)
    {
        if (tags is null)
        {
            return;
        }

        foreach (var tag in tags)
        {
            if (!string.IsNullOrWhiteSpace(tag))
            {
                lastSent.Remove(SuppressKey(readerId, tag.Trim()));
            }
        }
    }

    private static string SuppressKey(string readerId, string tagId) => $"{readerId}|EPC|{tagId}";

    private static async Task<InventoryOutcome> InventoryAsync(
        IYpd202Session? session,
        Func<CancellationToken, Task<IYpd202Session>> connect,
        CancellationToken cancellationToken)
    {
        try
        {
            session ??= await connect(cancellationToken);
            var result = await session.InventoryOnceAsync(cancellationToken);
            return InventoryOutcome.Ok(session, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            session?.Dispose();
            return InventoryOutcome.Fail(ex);
        }
    }

    private sealed record InventoryOutcome(bool Success, IYpd202Session? Session, Ypd202InventoryResult? Result, Exception? Error)
    {
        public static InventoryOutcome Ok(IYpd202Session session, Ypd202InventoryResult result)
            => new(true, session, result, null);

        public static InventoryOutcome Fail(Exception error) => new(false, null, null, error);
    }
}
