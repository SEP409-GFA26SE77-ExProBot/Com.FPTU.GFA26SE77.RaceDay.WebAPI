using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using RaceDay.RfidGateway;
using RaceDay.RfidGateway.Timing;
using RaceDay.RfidGateway.Ypd202;

Console.OutputEncoding = Encoding.UTF8;

GatewayArguments arguments;
try
{
    arguments = GatewayArguments.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(GatewayArguments.Usage);
    return 2;
}

if (arguments.Help)
{
    Console.WriteLine(GatewayArguments.Usage);
    return 0;
}

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = [],
    ContentRootPath = AppContext.BaseDirectory
});
var configuration = builder.Configuration;
var baseUrl = Required(configuration, "Api:BaseUrl");
var serviceKey = Required(configuration, "Api:ServiceKey");
var path = configuration["Api:TimingReadsPath"] ?? "/api/v1/race-monitoring/timing-reads";
var readerId = Required(configuration, "Reader:ReaderId");
var host = Required(configuration, "Reader:Host");
var port = int.Parse(Required(configuration, "Reader:Port"));
var pollInterval = TimeSpan.FromMilliseconds(int.Parse(configuration["Reader:PollIntervalMs"] ?? "1200"));
var suppressSeconds = int.Parse(configuration["Reader:RepeatSuppressSeconds"] ?? "15");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

using var sender = new TimingReadSender(baseUrl, path, serviceKey);

try
{
    if (arguments.Source == GatewayArguments.Test)
    {
        var scripted = await TimingGatewayLoop.RunAsync(
            new ScriptedTimingSource(arguments.BuildTestReads(readerId)),
            sender,
            TimeSpan.Zero,
            stopOnRejection: true,
            cts.Token);
        return scripted.Rejected > 0 ? 1 : 0;
    }

    var suppress = TimeSpan.FromSeconds(suppressSeconds);

    var readRequestsPath = configuration["Api:ReadRequestsPath"]
        ?? "/api/v1/race-monitoring/rfid/read-requests";
    using var commands = new RfidCommandClient(baseUrl, readRequestsPath, serviceKey);
    await Ypd202ReaderControlLoop.RunAsync(
        async cancellationToken => (IYpd202Session)await Ypd202Reader.ConnectAsync(host, port, cancellationToken),
        commands,
        sender,
        readerId,
        suppress,
        pollInterval,
        arguments.Polls,
        cts.Token);
    return 0;
}
catch (OperationCanceledException)
{
    Console.WriteLine("Đã dừng RFID Gateway.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static string Required(IConfiguration configuration, string key)
{
    var value = configuration[key];
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Thiếu cấu hình {key}.");
    }

    return value.Trim();
}
