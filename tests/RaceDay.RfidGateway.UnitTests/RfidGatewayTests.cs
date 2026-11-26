using System.Net;
using System.Text;
using System.Text.Json;
using RaceDay.RfidGateway;
using RaceDay.RfidGateway.Timing;
using RaceDay.RfidGateway.Ypd202;

namespace RaceDay.RfidGateway.UnitTests;

public class RfidGatewayTests
{
    private const string Epc = "E200470833106026A8490109";
    private const string ReaderId = "ypd-202-192.168.1.1";

    [Fact]
    public void Inventory_Command_Uses_The_Verified_Ypd202_Frame()
    {
        var frame = Ypd202Protocol.BuildFrame(
            Ypd202Protocol.HostType,
            Ypd202Protocol.InventoryCommand,
            ReadOnlySpan<byte>.Empty);

        Assert.Equal(new byte[] { 0xAA, 0x00, 0x22, 0x00, 0x00, 0x22, 0xDD }, frame);
    }

    [Fact]
    public void Notification_Frame_Parses_The_Demo_Epc()
    {
        var epc = Convert.FromHexString(Epc);
        var payload = new byte[5 + epc.Length];
        payload[0] = 0xC2;
        payload[1] = 0x30;
        payload[2] = 0x00;
        epc.CopyTo(payload.AsSpan(3));
        var pending = Ypd202Protocol.BuildFrame(0x02, Ypd202Protocol.InventoryCommand, payload).ToList();

        Assert.True(Ypd202Protocol.TryTakeFrame(pending, out var frame));
        var tag = Ypd202Protocol.ParseTag(frame!.Payload, DateTimeOffset.Parse("2026-11-19T04:18:00Z"));

        Assert.Empty(pending);
        Assert.NotNull(tag);
        Assert.Equal(Epc, tag.Epc);
        Assert.Equal(0xC2, tag.RssiRaw);
    }

    [Fact]
    public void Corrupt_Frame_Is_Rejected_And_Short_Payload_Is_Not_A_Tag()
    {
        var frame = Ypd202Protocol.BuildFrame(0x02, Ypd202Protocol.InventoryCommand, new byte[] { 1, 2, 3, 4, 5 });
        frame[^2] ^= 0xFF;
        var pending = frame.ToList();

        Assert.Throws<IOException>(() => Ypd202Protocol.TryTakeFrame(pending, out _));
        Assert.Null(Ypd202Protocol.ParseTag([0x01, 0x30, 0x00]));
    }

    [Fact]
    public async Task Test_Source_And_Ypd202_Adapter_Share_The_Send_Loop()
    {
        var sender = new RecordingSender();
        var observed = new DateTime(2026, 11, 19, 4, 18, 0, DateTimeKind.Utc);
        var scripted = new ScriptedTimingSource(
        [
            new TimingRead(Guid.NewGuid(), ReaderId, "EPC", Epc, observed, TimestampSources.TestClock)
        ]);
        var ypd = new Ypd202TimingSource(
            _ => Task.FromResult<IYpd202Session>(new StaticSession(new Ypd202InventoryResult(
                [new Ypd202TagObservation(Epc, 0xC2, new DateTimeOffset(observed))],
                "Confirmed"))),
            ReaderId,
            TimeSpan.FromMilliseconds(1),
            maxPolls: 1,
            _ => { });

        await TimingGatewayLoop.RunAsync(scripted, sender, TimeSpan.Zero, true, CancellationToken.None, _ => { });
        await TimingGatewayLoop.RunAsync(ypd, sender, TimeSpan.Zero, true, CancellationToken.None, _ => { });

        Assert.Equal(2, sender.Reads.Count);
        Assert.All(sender.Reads, read =>
        {
            Assert.Equal(ReaderId, read.ReaderId);
            Assert.Equal("EPC", read.TagScheme);
            Assert.Equal(Epc, read.TagId);
            Assert.NotEqual(Guid.Empty, read.ReadId);
        });
        Assert.Equal(TimestampSources.TestClock, sender.Reads[0].TimestampSource);
        Assert.Equal(TimestampSources.GatewayClock, sender.Reads[1].TimestampSource);
    }

    [Fact]
    public async Task Live_Window_Suppresses_A_Repeated_Epc_But_The_Same_ReadId_Is_Still_Delivered()
    {
        var observed = new DateTime(2026, 11, 19, 4, 18, 0, DateTimeKind.Utc);
        var readId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var repeated = new TimingRead(readId, ReaderId, "EPC", Epc, observed, TimestampSources.TestClock);
        var first = new RecordingSender();
        var second = new RecordingSender();

        var suppressed = await TimingGatewayLoop.RunAsync(
            new ScriptedTimingSource(
            [
                repeated,
                repeated with { ReadId = Guid.NewGuid(), ObservedAtUtc = observed.AddSeconds(1) }
            ]),
            first,
            TimeSpan.FromSeconds(15),
            false,
            CancellationToken.None,
            _ => { });
        var delivered = await TimingGatewayLoop.RunAsync(
            new ScriptedTimingSource([repeated, repeated]),
            second,
            TimeSpan.Zero,
            false,
            CancellationToken.None,
            _ => { });

        Assert.Equal(1, suppressed.Sent);
        Assert.Equal(1, suppressed.Suppressed);
        Assert.Equal(2, delivered.Sent);
        Assert.Equal(2, second.Reads.Count);
        Assert.All(second.Reads, read => Assert.Equal(readId, read.ReadId));
    }

    [Fact]
    public void Test_Arguments_Build_One_TimingRead_Contract()
    {
        var readId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var observed = new DateTime(2026, 11, 19, 4, 18, 0, DateTimeKind.Utc);
        var arguments = GatewayArguments.Parse(
            ["--source", "test", "--tag", "e200470833106026a8490109", "--read-id", readId.ToString(), "--repeat", "2"]);

        var reads = arguments.BuildTestReads(ReaderId, observed);

        Assert.Equal(2, reads.Count);
        Assert.All(reads, read =>
        {
            Assert.Equal(readId, read.ReadId);
            Assert.Equal(ReaderId, read.ReaderId);
            Assert.Equal("EPC", read.TagScheme);
            Assert.Equal(Epc, read.TagId);
            Assert.Equal(observed, read.ObservedAtUtc);
            Assert.Equal(TimestampSources.TestClock, read.TimestampSource);
        });
    }

    [Fact]
    public async Task Sender_Posts_The_TimingRead_Contract()
    {
        var readId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var handler = new CaptureHandler(HttpStatusCode.OK, Ack(readId, "Accepted"));
        using var sender = new TimingReadSender(
            "http://localhost:5080",
            "/api/v1/race-monitoring/timing-reads",
            "dev-rfid-gateway-key",
            handler);
        var read = new TimingRead(
            readId,
            ReaderId,
            "EPC",
            Epc,
            new DateTime(2026, 11, 19, 4, 18, 0, DateTimeKind.Utc),
            TimestampSources.TestClock);

        var ack = await sender.SendAsync(read, CancellationToken.None);
        var json = JsonSerializer.Deserialize<JsonElement>(handler.Body!, TimingReadJson.Options);

        Assert.Equal("Accepted", ack.Outcome);
        Assert.Equal(readId, ack.ReadId);
        Assert.Equal("http://localhost:5080/api/v1/race-monitoring/timing-reads", handler.RequestUri);
        Assert.Equal("dev-rfid-gateway-key", handler.ServiceKey);
        Assert.Equal(readId, json.GetProperty("readId").GetGuid());
        Assert.Equal(ReaderId, json.GetProperty("readerId").GetString());
        Assert.Equal("EPC", json.GetProperty("tagScheme").GetString());
        Assert.Equal(Epc, json.GetProperty("tagId").GetString());
        Assert.Equal(TimestampSources.TestClock, json.GetProperty("timestampSource").GetString());
        Assert.True(json.TryGetProperty("observedAtUtc", out _));
    }

    [Fact]
    public async Task Sender_Surfaces_An_Unassigned_Rejection()
    {
        var handler = new CaptureHandler(
            HttpStatusCode.BadRequest,
            """{"error":"Thẻ chưa được gán cho vận động viên trong phạm vi giải này."}""");
        using var sender = new TimingReadSender("http://localhost:5080", "/timing-reads", "dev-rfid-gateway-key", handler);

        var ex = await Assert.ThrowsAsync<TimingReadRejectedException>(() => sender.SendAsync(
            new TimingRead(Guid.NewGuid(), ReaderId, "EPC", "FFFFFFFFFFFFFFFFFFFFFFFF", DateTime.UtcNow, TimestampSources.TestClock),
            CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Thẻ chưa được gán", ex.Message);
    }

    [Fact]
    public async Task Assignment_Poll_Does_Not_Send_Timing_And_The_Next_Checkpoint_Is_Not_Suppressed()
    {
        var observed = new DateTime(2026, 11, 24, 4, 0, 0, DateTimeKind.Utc);
        var requestId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        Ypd202InventoryResult Inventory(DateTime at) => new(
            [new Ypd202TagObservation(Epc, 0xC2, new DateTimeOffset(at, TimeSpan.Zero))],
            "Confirmed");
        RfidCommandPoll Poll(string command, IReadOnlyList<string> release) =>
            new(command, requestId, command, null, null, ReaderId, release);

        var releaseOnly = new ScriptedCommands(
            Poll("checkpoint", []),
            Poll("assign", []),
            Poll("checkpoint", [Epc]));
        var sender = new RecordingSender();
        var released = await Ypd202ReaderControlLoop.RunAsync(
            _ => Task.FromResult<IYpd202Session>(new QueueSession(new Queue<Ypd202InventoryResult>(
            [
                Inventory(observed),
                Inventory(observed.AddSeconds(1)),
                Inventory(observed.AddSeconds(2))
            ]))),
            releaseOnly,
            sender,
            ReaderId,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMilliseconds(1),
            maxPolls: 3,
            CancellationToken.None,
            _ => { });

        Assert.Equal(2, released.TimingSent);
        Assert.Equal(0, released.TimingSuppressed);
        Assert.Equal(1, released.AssignmentPolls);
        Assert.Single(releaseOnly.Posted);
        Assert.Equal(Epc, releaseOnly.Posted[0][0].TagId);
        Assert.Equal(2, sender.Reads.Count);

        var held = new ScriptedCommands(
            Poll("assign", []),
            Poll("hold", [Epc]),
            Poll("checkpoint", [Epc]));
        var afterHold = new RecordingSender();
        var waited = await Ypd202ReaderControlLoop.RunAsync(
            _ => Task.FromResult<IYpd202Session>(new QueueSession(new Queue<Ypd202InventoryResult>(
            [
                Inventory(observed),
                Inventory(observed.AddSeconds(1))
            ]))),
            held,
            afterHold,
            ReaderId,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMilliseconds(1),
            maxPolls: 3,
            CancellationToken.None,
            _ => { });

        Assert.Equal(1, waited.HeldPolls);
        Assert.Equal(1, waited.AssignmentPolls);
        Assert.Equal(1, waited.TimingSent);
        Assert.Equal(0, waited.TimingSuppressed);
        Assert.Single(afterHold.Reads);
    }

    [Fact]
    public async Task Command_Client_Polls_With_The_Service_Key()
    {
        var requestId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        var handler = new CaptureHandler(
            HttpStatusCode.OK,
            $$"""{"command":"assign","requestId":"{{requestId}}","status":"Reading","releaseTagIds":[]}""");
        using var client = new RfidCommandClient(
            "http://localhost:5080",
            "/api/v1/race-monitoring/rfid/read-requests",
            "dev-rfid-gateway-key",
            handler);

        var poll = await client.PollAsync(ReaderId, requestId, CancellationToken.None);

        Assert.Equal("assign", poll.Command);
        Assert.Equal(requestId, poll.RequestId);
        Assert.Contains("/api/v1/race-monitoring/rfid/read-requests/next?readerId=", handler.RequestUri);
        Assert.DoesNotContain("/timing-reads", handler.RequestUri);
        Assert.Equal("dev-rfid-gateway-key", handler.ServiceKey);
    }

    private static string Ack(Guid readId, string outcome) =>
        $$"""
        {"outcome":"{{outcome}}","readId":"{{readId}}","scanId":"{{readId}}","raceEventLogId":"22222222-2222-4222-8222-222222222222","sequence":7,"eventId":"b8fdd6c9-c6a6-44d9-9958-a98c8094fba8","checkpointId":"f1d10000-0000-4000-8000-000000000001","registrationId":"44444444-dddd-4ddd-8ddd-ddddddddd000"}
        """;

    private sealed class StaticSession(Ypd202InventoryResult result) : IYpd202Session
    {
        public Task<Ypd202InventoryResult> InventoryOnceAsync(CancellationToken cancellationToken)
            => Task.FromResult(result);

        public void Dispose()
        {
        }
    }

    private sealed class QueueSession(Queue<Ypd202InventoryResult> results) : IYpd202Session
    {
        public Task<Ypd202InventoryResult> InventoryOnceAsync(CancellationToken cancellationToken)
        {
            if (results.Count == 0)
            {
                throw new InvalidOperationException("Hết kịch bản inventory.");
            }

            return Task.FromResult(results.Dequeue());
        }

        public void Dispose()
        {
        }
    }

    private sealed class ScriptedCommands(params RfidCommandPoll[] polls) : IRfidCommandClient
    {
        private readonly Queue<RfidCommandPoll> _polls = new(polls);
        public List<IReadOnlyList<RfidObservedTag>> Posted { get; } = [];

        public Task<RfidCommandPoll> PollAsync(string readerId, Guid? requestId, CancellationToken cancellationToken)
            => Task.FromResult(_polls.Dequeue());

        public Task PostObservationsAsync(
            Guid requestId,
            string readerId,
            IReadOnlyList<RfidObservedTag> tags,
            CancellationToken cancellationToken)
        {
            Posted.Add(tags);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSender : ITimingReadSender
    {
        public List<TimingRead> Reads { get; } = [];

        public Task<TimingReadAck> SendAsync(TimingRead read, CancellationToken cancellationToken)
        {
            Reads.Add(read);
            return Task.FromResult(new TimingReadAck(
                "Accepted",
                read.ReadId,
                read.ReadId,
                Guid.NewGuid(),
                1,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid()));
        }
    }

    private sealed class CaptureHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public string? RequestUri { get; private set; }
        public string? ServiceKey { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            ServiceKey = request.Headers.TryGetValues("X-Service-Key", out var values) ? values.Single() : null;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
