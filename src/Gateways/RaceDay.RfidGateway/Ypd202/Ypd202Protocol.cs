namespace RaceDay.RfidGateway.Ypd202;

public sealed record Ypd202Frame(byte Type, byte Command, byte[] Payload);

public sealed record Ypd202TagObservation(string Epc, byte RssiRaw, DateTimeOffset ObservedAt);

public sealed record Ypd202InventoryResult(
    IReadOnlyList<Ypd202TagObservation> Observations,
    string CompletionStatus);

/// <summary>
/// Khung AA TYPE CMD LEN_H LEN_L PAYLOAD CHECKSUM DD đã kiểm chứng với YPD-202/R200.
/// Inventory dùng lệnh 0x22. Payload thẻ là RSSI, PC, EPC, CRC.
/// </summary>
public static class Ypd202Protocol
{
    public const byte Header = 0xAA;
    public const byte Footer = 0xDD;
    public const byte HostType = 0x00;
    public const byte InventoryCommand = 0x22;

    public static byte[] BuildFrame(byte type, byte command, ReadOnlySpan<byte> payload)
    {
        var frame = new byte[payload.Length + 7];
        frame[0] = Header;
        frame[1] = type;
        frame[2] = command;
        frame[3] = (byte)(payload.Length >> 8);
        frame[4] = (byte)payload.Length;
        payload.CopyTo(frame.AsSpan(5));

        var checksum = 0;
        for (var i = 1; i < payload.Length + 5; i++)
        {
            checksum += frame[i];
        }

        frame[payload.Length + 5] = (byte)checksum;
        frame[payload.Length + 6] = Footer;
        return frame;
    }

    public static bool TryTakeFrame(List<byte> pending, out Ypd202Frame? frame)
    {
        frame = null;
        while (true)
        {
            var start = pending.IndexOf(Header);
            if (start < 0)
            {
                pending.Clear();
                return false;
            }

            if (start > 0)
            {
                pending.RemoveRange(0, start);
            }

            if (pending.Count < 7)
            {
                return false;
            }

            var length = (pending[3] << 8) | pending[4];
            if (length > 2048)
            {
                pending.RemoveAt(0);
                continue;
            }

            var frameLength = length + 7;
            if (pending.Count < frameLength)
            {
                return false;
            }

            var data = pending.GetRange(0, frameLength).ToArray();
            pending.RemoveRange(0, frameLength);

            var checksum = 0;
            for (var i = 1; i < length + 5; i++)
            {
                checksum += data[i];
            }

            if (data[^1] != Footer || (byte)checksum != data[^2])
            {
                throw new IOException("Khung RFID sai checksum hoặc footer.");
            }

            frame = new Ypd202Frame(data[1], data[2], data.AsSpan(5, length).ToArray());
            return true;
        }
    }

    public static Ypd202TagObservation? ParseTag(byte[] payload, DateTimeOffset? observedAt = null)
    {
        if (payload.Length < 7)
        {
            return null;
        }

        var pc = (payload[1] << 8) | payload[2];
        var epcBytes = ((pc >> 11) & 0x1F) * 2;
        if (epcBytes <= 0 || payload.Length != epcBytes + 5)
        {
            return null;
        }

        return new Ypd202TagObservation(
            Convert.ToHexString(payload.AsSpan(3, epcBytes)),
            payload[0],
            observedAt ?? DateTimeOffset.Now);
    }
}
