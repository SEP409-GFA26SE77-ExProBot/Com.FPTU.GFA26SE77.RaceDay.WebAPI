using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RaceDay.RfidGateway.Timing;

public sealed class RfidCommandClient : IRfidCommandClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _collectionUrl;
    private readonly string _serviceKey;

    public RfidCommandClient(string baseUrl, string readRequestsPath, string serviceKey, HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrWhiteSpace(serviceKey))
        {
            throw new InvalidOperationException("Thiếu Api:ServiceKey của RFID Gateway.");
        }

        _serviceKey = serviceKey;
        _collectionUrl = $"{baseUrl.TrimEnd('/')}/{readRequestsPath.TrimStart('/')}";
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<RfidCommandPoll> PollAsync(
        string readerId,
        Guid? requestId,
        CancellationToken cancellationToken)
    {
        var url = $"{_collectionUrl}/next?readerId={Uri.EscapeDataString(readerId)}";
        if (requestId is Guid id)
        {
            url += $"&requestId={id:D}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        Stamp(request);
        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Không kéo được lệnh đọc ({(int)response.StatusCode}): {body}");
        }

        return JsonSerializer.Deserialize<RfidCommandPoll>(body, TimingReadJson.Options)
            ?? throw new InvalidOperationException("Backend trả thân rỗng.");
    }

    public async Task PostObservationsAsync(
        Guid requestId,
        string readerId,
        IReadOnlyList<RfidObservedTag> tags,
        CancellationToken cancellationToken)
    {
        var url = $"{_collectionUrl}/{requestId:D}/observations";
        var json = JsonSerializer.Serialize(
            new { readerId, epcs = tags },
            TimingReadJson.Options);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        Stamp(request);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Không nộp được EPC ({(int)response.StatusCode}): {body}");
        }
    }

    public void Dispose() => _http.Dispose();

    private void Stamp(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation(RfidServiceHeaders.ServiceKey, _serviceKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }
}
