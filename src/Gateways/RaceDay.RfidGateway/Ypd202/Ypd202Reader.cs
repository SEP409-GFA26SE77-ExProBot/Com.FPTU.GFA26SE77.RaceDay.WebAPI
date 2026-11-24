using System.Diagnostics;
using System.Net.Sockets;

namespace RaceDay.RfidGateway.Ypd202;

public interface IYpd202Session : IDisposable
{
    Task<Ypd202InventoryResult> InventoryOnceAsync(CancellationToken cancellationToken);
}

/// <summary>
/// TCP client của YPD-202/R200. Chỉ inventory, không đổi công suất RF.
/// </summary>
public sealed class Ypd202Reader : IYpd202Session
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly List<byte> _pending = [];
    private bool _faulted;

    private Ypd202Reader(TcpClient tcp)
    {
        _tcp = tcp;
        _stream = tcp.GetStream();
    }

    public static async Task<Ypd202Reader> ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            await client.ConnectAsync(host, port, timeout.Token);
            return new Ypd202Reader(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task<Ypd202InventoryResult> InventoryOnceAsync(CancellationToken cancellationToken)
    {
        EnsureReady();
        var observations = new List<Ypd202TagObservation>();
        try
        {
            await SendAsync(Ypd202Protocol.InventoryCommand, [], cancellationToken);
            var watch = Stopwatch.StartNew();
            var gotAnyResponse = false;
            while (watch.ElapsedMilliseconds < 2500)
            {
                var remaining = 2500 - (int)watch.ElapsedMilliseconds;
                if (gotAnyResponse)
                {
                    remaining = Math.Min(600, remaining);
                }

                if (remaining <= 0)
                {
                    break;
                }

                Ypd202Frame frame;
                try
                {
                    frame = await ReceiveFrameAsync(remaining, cancellationToken);
                }
                catch (TimeoutException) when (gotAnyResponse)
                {
                    break;
                }

                gotAnyResponse = true;
                if (frame.Type == 0x01 && frame.Command == 0xFF)
                {
                    var error = frame.Payload.Length > 0 ? frame.Payload[0] : (byte)0;
                    if (error == 0x15)
                    {
                        return new Ypd202InventoryResult(observations, "Confirmed");
                    }

                    throw new IOException($"Inventory error 0x{error:X2}");
                }

                if (frame.Type == 0x02 && frame.Command == Ypd202Protocol.InventoryCommand)
                {
                    var tag = Ypd202Protocol.ParseTag(frame.Payload);
                    if (tag is not null)
                    {
                        observations.Add(tag);
                    }

                    continue;
                }

                if (frame.Type == 0x01 && frame.Command == Ypd202Protocol.InventoryCommand)
                {
                    if (frame.Payload.Length > 0 && frame.Payload[0] != 0x00)
                    {
                        throw new IOException($"Inventory status 0x{frame.Payload[0]:X2}");
                    }

                    return new Ypd202InventoryResult(observations, "Confirmed");
                }

                throw new IOException(
                    $"Unexpected inventory frame: T=0x{frame.Type:X2}, C=0x{frame.Command:X2}");
            }

            if (!gotAnyResponse)
            {
                throw new TimeoutException("Không có phản hồi inventory. Trạng thái đầu đọc không xác định.");
            }

            return new Ypd202InventoryResult(observations, "Unconfirmed");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            _faulted = true;
            throw;
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
        _tcp.Dispose();
    }

    private async Task SendAsync(byte command, byte[] payload, CancellationToken cancellationToken)
    {
        EnsureReady();
        var frame = Ypd202Protocol.BuildFrame(Ypd202Protocol.HostType, command, payload);
        await _stream.WriteAsync(frame, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    private async Task<Ypd202Frame> ReceiveFrameAsync(int timeoutMs, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var buffer = new byte[1024];
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            if (Ypd202Protocol.TryTakeFrame(_pending, out var parsed))
            {
                return parsed!;
            }

            var remaining = Math.Max(1, timeoutMs - (int)watch.ElapsedMilliseconds);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(remaining);
            int count;
            try
            {
                count = await _stream.ReadAsync(buffer.AsMemory(), timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (count == 0)
            {
                throw new IOException("Đầu đọc đã đóng kết nối TCP.");
            }

            _pending.AddRange(buffer.AsSpan(0, count).ToArray());
            if (_pending.Count > 16384)
            {
                throw new IOException("Bộ đệm nhận RFID quá lớn.");
            }
        }

        throw new TimeoutException("Không nhận được khung hợp lệ từ đầu đọc RFID.");
    }

    private void EnsureReady()
    {
        if (_faulted)
        {
            throw new IOException("Đầu đọc đang ở trạng thái Faulted.");
        }
    }
}
