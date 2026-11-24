using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RaceDay.RfidGateway.Timing;

public interface ITimingReadSender
{
    Task<TimingReadAck> SendAsync(TimingRead read, CancellationToken cancellationToken);
}

public sealed record TimingReadAck(
    string Outcome,
    Guid ReadId,
    Guid ScanId,
    Guid? RaceEventLogId,
    long? Sequence,
    Guid EventId,
    Guid CheckpointId,
    Guid RegistrationId);

public sealed class TimingReadRejectedException(int statusCode, string body)
    : Exception($"Backend từ chối TimingRead ({statusCode}): {body}")
{
    public int StatusCode { get; } = statusCode;
}

public static class TimingReadJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public sealed class TimingReadSender : ITimingReadSender, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _url;
    private readonly string _serviceKey;

    public TimingReadSender(string baseUrl, string path, string serviceKey, HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrWhiteSpace(serviceKey))
        {
            throw new InvalidOperationException("Thiếu Api:ServiceKey của RFID Gateway.");
        }

        _serviceKey = serviceKey;
        _url = $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<TimingReadAck> SendAsync(TimingRead read, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(read, TimingReadJson.Options);
        using var request = new HttpRequestMessage(HttpMethod.Post, _url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(RfidServiceHeaders.ServiceKey, _serviceKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new TimingReadRejectedException((int)response.StatusCode, body);
        }

        return JsonSerializer.Deserialize<TimingReadAck>(body, TimingReadJson.Options)
            ?? throw new InvalidOperationException("Backend trả thân rỗng.");
    }

    public void Dispose() => _http.Dispose();
}

public static class RfidServiceHeaders
{
    public const string ServiceKey = "X-Service-Key";
}
