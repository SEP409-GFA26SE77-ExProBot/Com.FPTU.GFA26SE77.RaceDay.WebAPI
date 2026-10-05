using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Payment.Application;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Payment.Infrastructure.PayOs;

public sealed class PayOsOptions
{
    public const string SectionName = "PayOS";
    public string ClientId { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string ChecksumKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api-merchant.payos.vn";
    /// <summary>When true or keys missing → mock checkout (testable without PayOS account).</summary>
    public bool UseMock { get; set; }
    public string? PublicApiBaseUrl { get; set; }
}

public sealed class PayOsGateway(
    HttpClient http,
    IConfiguration configuration,
    ILogger<PayOsGateway> logger) : IPayOsGateway
{
    private PayOsOptions Options
    {
        get
        {
            var o = new PayOsOptions();
            configuration.GetSection(PayOsOptions.SectionName).Bind(o);
            if (string.IsNullOrWhiteSpace(o.PublicApiBaseUrl))
            {
                o.PublicApiBaseUrl = configuration["Storage:PublicBaseUrl"] ?? "http://localhost:5080";
            }

            return o;
        }
    }

    public bool IsMock
    {
        get
        {
            var o = Options;
            return o.UseMock
                   || string.IsNullOrWhiteSpace(o.ClientId)
                   || string.IsNullOrWhiteSpace(o.ApiKey)
                   || string.IsNullOrWhiteSpace(o.ChecksumKey);
        }
    }

    public async Task<PayOsCreateResult> CreatePaymentLinkAsync(PayOsCreateRequest request, CancellationToken cancellationToken = default)
    {
        if (IsMock)
        {
            var baseUrl = Options.PublicApiBaseUrl!.TrimEnd('/');
            // Same contract as live: checkoutUrl + paymentLinkId; FE opens URL then returnUrl?code=00&status=PAID&orderCode=
            var checkout =
                $"{baseUrl}/api/v1/payment/mock-checkout/{request.PaymentId}"
                + $"?returnUrl={Uri.EscapeDataString(request.ReturnUrl)}"
                + $"&cancelUrl={Uri.EscapeDataString(request.CancelUrl)}";
            logger.LogWarning("PayOS mock mode (dev) — checkout behaves like live redirect flow: {Url}", checkout);
            return new PayOsCreateResult(checkout, $"mock-{request.OrderCode}", "{\"mock\":true,\"status\":\"PENDING\"}");
        }

        var o = Options;
        var amount = request.AmountVnd;
        // PayOS: non-linked bank transfer description max 9 chars.
        var description = Truncate(request.Description, 9);
        var orderCode = request.OrderCode;
        var cancelUrl = request.CancelUrl;
        var returnUrl = request.ReturnUrl;

        var signature = Sign(
            $"amount={amount}&cancelUrl={cancelUrl}&description={description}&orderCode={orderCode}&returnUrl={returnUrl}",
            o.ChecksumKey);

        var payload = new
        {
            orderCode,
            amount,
            description,
            cancelUrl,
            returnUrl,
            signature
        };

        using var msg = new HttpRequestMessage(HttpMethod.Post, $"{o.BaseUrl.TrimEnd('/')}/v2/payment-requests");
        msg.Headers.TryAddWithoutValidation("x-client-id", o.ClientId);
        msg.Headers.TryAddWithoutValidation("x-api-key", o.ApiKey);
        msg.Content = JsonContent.Create(payload);

        using var resp = await http.SendAsync(msg, cancellationToken);
        var raw = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogError("PayOS create failed {Status}: {Body}", (int)resp.StatusCode, raw);
            throw new DomainException($"Không tạo được link PayOS: {ExtractPayOsError(raw)}");
        }

        using var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.TryGetProperty("code", out var codeEl)
            && codeEl.GetString() is { } code
            && code != "00")
        {
            var desc = doc.RootElement.TryGetProperty("desc", out var d) ? d.GetString() : raw;
            throw new DomainException($"PayOS từ chối tạo link: {desc}");
        }

        var data = doc.RootElement.GetProperty("data");
        var checkoutUrl = data.GetProperty("checkoutUrl").GetString()
            ?? throw new DomainException("PayOS không trả checkoutUrl.");
        var paymentLinkId = data.TryGetProperty("paymentLinkId", out var pl) ? pl.GetString() : null;
        return new PayOsCreateResult(checkoutUrl, paymentLinkId, raw);
    }

    public async Task<PayOsPaymentLinkInfo?> GetPaymentLinkAsync(long orderCode, CancellationToken cancellationToken = default)
    {
        if (IsMock)
        {
            return null;
        }

        var o = Options;
        using var msg = new HttpRequestMessage(HttpMethod.Get, $"{o.BaseUrl.TrimEnd('/')}/v2/payment-requests/{orderCode}");
        msg.Headers.TryAddWithoutValidation("x-client-id", o.ClientId);
        msg.Headers.TryAddWithoutValidation("x-api-key", o.ApiKey);

        using var resp = await http.SendAsync(msg, cancellationToken);
        var raw = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("PayOS get link failed {Status}: {Body}", (int)resp.StatusCode, raw);
            return null;
        }

        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var status = data.TryGetProperty("status", out var st) ? st.GetString() : null;
        var id = data.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        return new PayOsPaymentLinkInfo(orderCode, status, id, raw);
    }

    public bool VerifyWebhookSignature(string rawBody, string? signatureHeader)
    {
        if (IsMock)
        {
            return true;
        }

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBody) ? "{}" : rawBody);
            var root = doc.RootElement;
            var signature = signatureHeader;
            if (string.IsNullOrWhiteSpace(signature)
                && root.TryGetProperty("signature", out var sigEl))
            {
                signature = sigEl.GetString();
            }

            if (string.IsNullOrWhiteSpace(signature) || !root.TryGetProperty("data", out var data))
            {
                return false;
            }

            // Official: HMAC_SHA256(sorted key=value of data object, checksumKey)
            var expected = Sign(SortObjectToQuery(data), Options.ChecksumKey);
            return FixedEquals(expected, signature);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PayOS webhook signature verify failed");
            return false;
        }
    }

    private static string SortObjectToQuery(JsonElement data)
    {
        var parts = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in data.EnumerateObject())
        {
            parts[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.Null => string.Empty,
                JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                JsonValueKind.Number => prop.Value.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => prop.Value.ToString()
            };
        }

        return string.Join('&', parts.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    private static string Sign(string data, string checksumKey)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(checksumKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool FixedEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        if (ba.Length != bb.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    private static string Truncate(string value, int max)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return "RaceDay";
        }

        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private static string ExtractPayOsError(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("desc", out var d))
            {
                return d.GetString() ?? raw;
            }
        }
        catch
        {
            // ignore
        }

        return raw.Length > 200 ? raw[..200] : raw;
    }
}
