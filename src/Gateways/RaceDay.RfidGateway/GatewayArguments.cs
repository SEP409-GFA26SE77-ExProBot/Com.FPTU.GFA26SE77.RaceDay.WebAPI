using RaceDay.RfidGateway.Timing;

namespace RaceDay.RfidGateway;

public sealed record GatewayArguments(
    string Source,
    IReadOnlyList<string> Tags,
    Guid? ReadId,
    int Repeat,
    int? Polls,
    bool Help)
{
    public const string Ypd202 = "ypd202";
    public const string Test = "test";

    public static GatewayArguments Parse(string[] args)
    {
        var source = Ypd202;
        var tags = new List<string>();
        Guid? readId = null;
        var repeat = 1;
        int? polls = null;
        var help = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help":
                case "-h":
                    help = true;
                    break;
                case "--source":
                    source = Next(args, ref i, arg).Trim().ToLowerInvariant();
                    break;
                case "--tag":
                    tags.Add(Next(args, ref i, arg));
                    break;
                case "--read-id":
                    if (!Guid.TryParse(Next(args, ref i, arg), out var parsed))
                    {
                        throw new ArgumentException("--read-id phải là GUID.");
                    }

                    readId = parsed;
                    break;
                case "--repeat":
                    repeat = ReadPositive(Next(args, ref i, arg), arg);
                    break;
                case "--polls":
                    polls = ReadPositive(Next(args, ref i, arg), arg);
                    break;
                default:
                    throw new ArgumentException($"Tham số không hỗ trợ: {arg}");
            }
        }

        if (source is not (Ypd202 or Test))
        {
            throw new ArgumentException("--source chỉ nhận ypd202 hoặc test.");
        }

        if (source == Test && tags.Count == 0 && !help)
        {
            throw new ArgumentException("Nguồn test cần ít nhất một --tag.");
        }

        if (readId is not null && tags.Count != 1)
        {
            throw new ArgumentException("--read-id chỉ dùng với đúng một --tag.");
        }

        return new GatewayArguments(source, tags, readId, repeat, polls, help);
    }

    public IReadOnlyList<TimingRead> BuildTestReads(string readerId, DateTime? observedAtUtc = null)
    {
        if (Tags.Count == 0)
        {
            throw new InvalidOperationException("Nguồn test chưa có thẻ.");
        }

        var observed = observedAtUtc ?? DateTime.UtcNow;
        if (observed.Kind != DateTimeKind.Utc)
        {
            observed = observed.ToUniversalTime();
        }

        var reads = new List<TimingRead>();
        var sharedId = ReadId ?? Guid.NewGuid();
        for (var copy = 0; copy < Repeat; copy++)
        {
            foreach (var tag in Tags)
            {
                var id = Tags.Count == 1 ? sharedId : Guid.NewGuid();
                reads.Add(new TimingRead(
                    id,
                    readerId,
                    "EPC",
                    NormalizeEpc(tag),
                    observed,
                    TimestampSources.TestClock));
            }
        }

        return reads;
    }

    public static string Usage { get; } =
        """
        RaceDay RFID Gateway

          --source ypd202 [--polls N]
          --source test --tag EPC [--read-id GUID] [--repeat N]

        Mặc định đọc YPD-202 cho đến khi Ctrl+C.
        Nguồn test gửi TimingRead qua vòng lặp Phase 1, cửa sổ lọc bằng 0.
        Nguồn ypd202 kéo yêu cầu gán thẻ; khi không có yêu cầu thì gửi TimingRead như Phase 1.
        Sau khi xác nhận gán, đầu đọc chờ lệnh bắt đầu quét checkpoint.
        """;

    private static string NormalizeEpc(string tag)
    {
        var hex = tag.Trim().Replace(" ", "", StringComparison.Ordinal).Replace(":", "", StringComparison.Ordinal)
            .ToUpperInvariant();
        if (hex.Length < 4 || hex.Length % 2 != 0 || hex.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException($"EPC không hợp lệ: {tag}");
        }

        return hex;
    }

    private static string Next(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Thiếu giá trị cho {name}.");
        }

        index++;
        return args[index];
    }

    private static int ReadPositive(string raw, string name)
    {
        if (!int.TryParse(raw, out var value) || value < 1)
        {
            throw new ArgumentException($"{name} phải là số nguyên lớn hơn 0.");
        }

        return value;
    }
}
