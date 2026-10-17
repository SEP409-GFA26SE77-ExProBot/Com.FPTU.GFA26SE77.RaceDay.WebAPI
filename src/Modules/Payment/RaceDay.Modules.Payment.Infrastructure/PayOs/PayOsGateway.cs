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
    /// <summary>Kênh Chi (payout) — fallback về ClientId/ApiKey/ChecksumKey nếu trống.</summary>
    public string? PayoutClientId { get; set; }
    public string? PayoutApiKey { get; set; }
    public string? PayoutChecksumKey { get; set; }
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
            // VietQR payload giả để FE render QR + thông tin CK chung 1 khung
            var mockQr =
                $"00020101021238570010A000000727012700069704220113MOCK{request.PaymentId:N}0208QRIBFTTA5303704540{request.AmountVnd:D}5802VN62{Math.Min(99, request.Description.Length + 4):D2}08{Truncate(request.Description, 25)}6304ABCD";
            var mockAccount = $"VQR{request.OrderCode % 1000000000:D9}";
            var mockDesc = Truncate(request.Description, 9);
            var mockRaw = System.Text.Json.JsonSerializer.Serialize(new
            {
                code = "00",
                desc = "success",
                mock = true,
                data = new
                {
                    checkoutUrl = checkout,
                    paymentLinkId = $"mock-{request.OrderCode}",
                    qrCode = mockQr,
                    accountNumber = mockAccount,
                    accountName = "RACEDAY PAYOS MOCK",
                    bin = "970422",
                    amount = request.AmountVnd,
                    description = mockDesc,
                    status = "PENDING"
                }
            });
            return new PayOsCreateResult(
                checkout,
                $"mock-{request.OrderCode}",
                mockRaw,
                mockQr,
                AccountNumber: mockAccount,
                AccountName: "RACEDAY PAYOS MOCK",
                Bin: "970422",
                TransferDescription: mockDesc);
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
        var qrCode = data.TryGetProperty("qrCode", out var qr) ? qr.GetString() : null;
        var accountNumber = data.TryGetProperty("accountNumber", out var an) ? an.GetString() : null;
        var accountName = data.TryGetProperty("accountName", out var am) ? am.GetString() : null;
        var bin = data.TryGetProperty("bin", out var b) ? b.GetString() : null;
        var transferDesc = data.TryGetProperty("description", out var td) ? td.GetString() : null;
        return new PayOsCreateResult(
            checkoutUrl, paymentLinkId, raw, qrCode, accountNumber, accountName, bin, transferDesc);
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

    public async Task<PayOsRefundResult> RefundAsync(
        PayOsRefundRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.AmountVnd <= 0)
        {
            throw new DomainException("Số tiền hoàn phải > 0.");
        }

        if (IsMock)
        {
            var mockId = $"mock-refund-{request.PaymentId:N}";
            var mockRaw = JsonSerializer.Serialize(new
            {
                code = "00",
                desc = "success",
                mock = true,
                data = new
                {
                    id = mockId,
                    referenceId = $"refund-{request.OrderCode}",
                    amount = request.AmountVnd,
                    description = Truncate(request.Description, 25),
                    approvalState = "SUCCEEDED"
                }
            });
            logger.LogWarning(
                "PayOS mock refund — không chuyển ngân hàng thật. payment={PaymentId} amount={Amount} payoutId={PayoutId}",
                request.PaymentId,
                request.AmountVnd,
                mockId);
            return new PayOsRefundResult(IsMock: true, mockId, mockRaw);
        }

        var (toBin, toAccount) = ResolveRefundDestination(request);
        if (string.IsNullOrWhiteSpace(toBin) || string.IsNullOrWhiteSpace(toAccount))
        {
            throw new DomainException(
                "Không có số tài khoản đối ứng từ PayOS webhook để hoàn tiền tự động. "
                + "Chỉ một số ngân hàng trả counterAccount — cần hoàn thủ công hoặc cấu hình lệnh chi với STK VĐV.");
        }

        var o = Options;
        var clientId = string.IsNullOrWhiteSpace(o.PayoutClientId) ? o.ClientId : o.PayoutClientId!;
        var apiKey = string.IsNullOrWhiteSpace(o.PayoutApiKey) ? o.ApiKey : o.PayoutApiKey!;
        var checksum = string.IsNullOrWhiteSpace(o.PayoutChecksumKey) ? o.ChecksumKey : o.PayoutChecksumKey!;

        var referenceId = $"refund-{request.OrderCode}-{request.AmountVnd}";
        var description = Truncate(request.Description, 25);
        var payload = new Dictionary<string, object?>
        {
            ["referenceId"] = referenceId,
            ["amount"] = request.AmountVnd,
            ["description"] = description,
            ["toBin"] = toBin.Trim(),
            ["toAccountNumber"] = toAccount.Trim()
        };

        var signature = SignPayoutPayload(payload, checksum);
        var idempotency = $"{request.PaymentId:N}-{request.AmountVnd}";

        using var msg = new HttpRequestMessage(HttpMethod.Post, $"{o.BaseUrl.TrimEnd('/')}/v1/payouts");
        msg.Headers.TryAddWithoutValidation("x-client-id", clientId);
        msg.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        msg.Headers.TryAddWithoutValidation("x-idempotency-key", idempotency);
        msg.Headers.TryAddWithoutValidation("x-signature", signature);
        msg.Content = JsonContent.Create(payload);

        using var resp = await http.SendAsync(msg, cancellationToken);
        var raw = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogError("PayOS payout (refund) failed {Status}: {Body}", (int)resp.StatusCode, raw);
            throw new DomainException($"Không tạo được lệnh chi hoàn tiền PayOS: {ExtractPayOsError(raw)}");
        }

        using var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.TryGetProperty("code", out var codeEl)
            && codeEl.GetString() is { } code
            && code != "00")
        {
            var desc = doc.RootElement.TryGetProperty("desc", out var d) ? d.GetString() : raw;
            throw new DomainException($"PayOS từ chối lệnh chi hoàn tiền: {desc}");
        }

        string? payoutId = null;
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            payoutId = data.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        }

        logger.LogInformation(
            "PayOS payout refund created payment={PaymentId} amount={Amount} payoutId={PayoutId}",
            request.PaymentId,
            request.AmountVnd,
            payoutId);
        return new PayOsRefundResult(IsMock: false, payoutId, raw);
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

    private static (string? ToBin, string? ToAccount) ResolveRefundDestination(PayOsRefundRequest request)
    {
        var bin = request.CounterAccountBankId;
        var account = request.CounterAccountNumber;
        if (!string.IsNullOrWhiteSpace(bin) && !string.IsNullOrWhiteSpace(account))
        {
            return (bin, account);
        }

        return TryExtractCounterAccount(request.ChargeRawJson);
    }

    internal static (string? ToBin, string? ToAccount) TryExtractCounterAccount(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return (null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            var data = root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object
                ? d
                : root;

            var bin = GetStringProp(data, "counterAccountBankId")
                      ?? GetStringProp(root, "counterAccountBankId");
            var account = GetStringProp(data, "counterAccountNumber")
                          ?? GetStringProp(root, "counterAccountNumber");
            return (
                string.IsNullOrWhiteSpace(bin) ? null : bin,
                string.IsNullOrWhiteSpace(account) ? null : account);
        }
        catch
        {
            return (null, null);
        }
    }

    private static string? GetStringProp(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    /// <summary>PayOS payout signature: deep-sort + encodeURIComponent(key)=encodeURIComponent(value) + HMAC-SHA256.</summary>
    private static string SignPayoutPayload(IReadOnlyDictionary<string, object?> payload, string checksumKey)
    {
        var parts = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in payload)
        {
            parts[key] = value switch
            {
                null => string.Empty,
                string s => s,
                int or long or decimal => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                bool b => b ? "true" : "false",
                _ => JsonSerializer.Serialize(value)
            };
        }

        var query = string.Join(
            '&',
            parts.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return Sign(query, checksumKey);
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
