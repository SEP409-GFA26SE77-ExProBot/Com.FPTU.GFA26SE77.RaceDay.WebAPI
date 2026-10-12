using System.Text;
using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Payment.Application;
using RaceDay.Modules.Payment.Infrastructure;
using RaceDay.Modules.Payment.Infrastructure.PayOs;
using RaceDay.Shared.Domain.Exceptions;
using RaceDay.Shared.Web.Auth;
using RaceDay.Shared.Web.OpenApi;

namespace RaceDay.Modules.Payment.Api;

public static class PaymentModule
{
    public static IServiceCollection AddPaymentModule(this IServiceCollection services, IConfiguration configuration)
    {
        // MediatR đăng ký tập trung ở Host (Program.cs)
        services.AddValidatorsFromAssembly(typeof(CreateCheckoutCommand).Assembly);
        services.AddPaymentInfrastructure(configuration);
        return services;
    }

    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1/payment")
            .WithTags(ApiTags.Payment)
            .RequireRateLimiting("public");

        api.MapGet("/status", (IPayOsGateway payOs) =>
            Results.Ok(new
            {
                module = "payment",
                status = "ready",
                payOsMode = payOs.IsMock ? "mock" : "live"
            }))
            .WithSummary("Trạng thái module Payment + mode PayOS (mock/live)");

        api.MapPost("/checkout", async (CreateCheckoutRequest body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new CreateCheckoutCommand(body.RegistrationId, body.ReturnUrl, body.CancelUrl), ct);
            return Results.Ok(result);
        })
        .RequireAuthorization(AuthPolicies.PublicUser)
        .WithSummary("Tạo link thanh toán PayOS cho đăng ký (pending_payment)")
        .WithDescription(
            "FE: sau POST /event/{id}/registrations → gọi checkout với registrationId + returnUrl/cancelUrl. "
            + "Mock mode: checkoutUrl mở trang giả PayOS (nút Thanh toán / Hủy).");

        api.MapPost("/checkout/package", async (CreatePackageCheckoutRequest body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new CreatePackageCheckoutCommand(body.PackagePurchaseId, body.ReturnUrl, body.CancelUrl), ct);
            return Results.Ok(result);
        })
        .RequireAuthorization(AuthPolicies.PublicUser)
        .WithSummary("Tạo link thanh toán PayOS cho gói sự kiện (event package)")
        .WithDescription(
            "FE: sau POST /event/organizer/packages/purchases → gọi checkout/package. "
            + "Mock mode: giống registration checkout.");

        api.MapGet("/mine", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListMyPaymentsQuery(), ct)))
            .RequireAuthorization(AuthPolicies.PublicUser)
            .WithSummary("Lịch sử payment của tôi (FE /payments)");

        api.MapGet("/{paymentId:guid}", async (Guid paymentId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GetPaymentByIdQuery(paymentId), ct)))
            .RequireAuthorization(AuthPolicies.PublicUser)
            .WithSummary("Chi tiết 1 payment");

        api.MapPost("/{paymentId:guid}/sync", async (Guid paymentId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new SyncPaymentFromPayOsCommand(paymentId), ct)))
            .RequireAuthorization(AuthPolicies.PublicUser)
            .WithSummary("Đồng bộ trạng thái từ PayOS (sau returnUrl / khi chưa có webhook)")
            .WithDescription(
                "Live: FE gọi sau khi user thanh toán xong và quay về returnUrl. "
                + "Nếu PayOS status=PAID → đăng ký chuyển pending_approval. "
                + "Khuyến nghị vẫn cấu hình webhook public (ngrok) cho production.");

        // Mock PayOS UI — cùng hành vi live: pay → ConfirmPaymentPaid → redirect returnUrl?code=00&status=PAID&orderCode=
        api.MapGet("/mock-checkout/{paymentId:guid}", async (
            Guid paymentId,
            string? returnUrl,
            string? cancelUrl,
            IPaymentRepository payments,
            CancellationToken ct) =>
        {
            var payment = await payments.GetByIdAsync(paymentId, ct);
            if (payment is null)
            {
                return Results.NotFound(new { message = "Payment not found" });
            }

            var html = BuildMockCheckoutHtml(PaymentDtoMapper.ToDto(payment), returnUrl, cancelUrl);
            return Results.Content(html, "text/html", Encoding.UTF8);
        })
        .AllowAnonymous()
        .ExcludeFromDescription();

        api.MapPost("/mock-checkout/{paymentId:guid}/pay", async (Guid paymentId, IMediator mediator, CancellationToken ct) =>
        {
            // Same payload shape as PayOS webhook data (code=00 / PAID)
            var raw = $"{{\"code\":\"00\",\"desc\":\"success\",\"success\":true,\"data\":{{\"orderCode\":0,\"amount\":0,\"code\":\"00\",\"desc\":\"Thanh cong\",\"paymentLinkId\":\"mock-{paymentId:N}\",\"status\":\"PAID\"}},\"signature\":\"mock\"}}";
            var paid = await mediator.Send(
                new ConfirmPaymentPaidCommand(paymentId, null, $"mock-{paymentId:N}", raw),
                ct);
            return Results.Ok(paid);
        })
        .AllowAnonymous()
        .WithSummary("Mock: giả lập PayOS PAID — cùng ConfirmPaymentPaid như webhook live")
        .WithDescription(
            "Dev only (UseMock=true). Luồng giống live: ConfirmPaymentPaid → PaymentCompleted → Event pending_approval. "
            + "Sau đó FE gọi POST /{id}/sync (no-op nếu đã paid) giống returnUrl flow.");

        api.MapPost("/mock-checkout/{paymentId:guid}/cancel", async (Guid paymentId, IPaymentRepository payments, IPaymentUnitOfWork uow, CancellationToken ct) =>
        {
            var payment = await payments.GetByIdAsync(paymentId, ct)
                ?? throw new InvalidOperationException("Payment not found");
            payment.MarkCancelled();
            await uow.SaveChangesAsync(ct);
            return Results.Ok(PaymentDtoMapper.ToDto(payment));
        })
        .AllowAnonymous()
        .ExcludeFromDescription();

        api.MapPost("/webhooks/payos", async (
            HttpRequest http,
            IPayOsGateway payOs,
            IMediator mediator,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            // PayOS pings this URL when saving webhook (sample code=00). Must return 2xx
            // even if orderCode is unknown — otherwise dashboard shows 400 "không hoạt động".
            var logger = loggerFactory.CreateLogger("PayOsWebhook");
            using var reader = new StreamReader(http.Body);
            var raw = await reader.ReadToEndAsync(ct);
            var signature = http.Headers["x-payos-signature"].FirstOrDefault()
                            ?? http.Headers["x-signature"].FirstOrDefault();

            if (!payOs.IsMock && !payOs.VerifyWebhookSignature(raw, signature))
            {
                logger.LogWarning("PayOS webhook rejected: invalid signature");
                return Results.Unauthorized();
            }

            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
            var root = doc.RootElement;
            var data = root.TryGetProperty("data", out var d) ? d : root;
            var orderCode = data.TryGetProperty("orderCode", out var oc) && oc.ValueKind == JsonValueKind.Number
                ? oc.GetInt64()
                : 0L;
            var status = data.TryGetProperty("status", out var st) ? st.GetString() : null;
            var code = data.TryGetProperty("code", out var c) ? c.GetString()
                : root.TryGetProperty("code", out var c2) ? c2.GetString() : null;

            var looksPaid = string.Equals(status, "PAID", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(code, "00", StringComparison.OrdinalIgnoreCase);

            if (looksPaid && orderCode > 0)
            {
                var gatewayId = data.TryGetProperty("paymentLinkId", out var pl) ? pl.GetString()
                    : data.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                try
                {
                    await mediator.Send(new ConfirmPaymentPaidCommand(null, orderCode, gatewayId, raw), ct);
                }
                catch (DomainException ex)
                {
                    // Confirm-webhook sample / unknown order → still ACK so PayOS accepts the URL.
                    logger.LogInformation(
                        ex,
                        "PayOS webhook ACK without matching payment (orderCode={OrderCode})",
                        orderCode);
                }
            }

            return Results.Ok(new { success = true, received = true });
        })
        .AllowAnonymous()
        .WithSummary("Webhook PayOS (PAID → Event MarkPaidPendingApproval)")
        .DisableAntiforgery();

        return app;
    }

    private static string BuildMockCheckoutHtml(PaymentDto payment, string? returnUrl, string? cancelUrl)
    {
        var ret = string.IsNullOrWhiteSpace(returnUrl) ? "" : returnUrl;
        var can = string.IsNullOrWhiteSpace(cancelUrl) ? "" : cancelUrl;
        return $$"""
            <!DOCTYPE html>
            <html lang="vi"><head><meta charset="utf-8"/><title>payOS Checkout</title>
            <style>
              body{font-family:system-ui;background:#0b1220;color:#e2e8f0;display:flex;min-height:100vh;align-items:center;justify-content:center;margin:0}
              .card{background:#111827;border:1px solid #1f2937;border-radius:16px;padding:28px;max-width:420px;width:90%;box-shadow:0 20px 50px #0008}
              .brand{color:#38bdf8;font-weight:800;letter-spacing:.08em;font-size:.75rem}
              h1{font-size:1.2rem;margin:8px 0} .muted{color:#94a3b8;font-size:.875rem}
              .amt{font-size:1.75rem;font-weight:800;margin:16px 0;color:#f8fafc}
              button{width:100%;padding:12px;border:0;border-radius:10px;font-weight:700;cursor:pointer;margin-top:8px}
              .pay{background:#0ea5e9;color:#082f49} .cancel{background:#334155;color:#e2e8f0}
              #msg{margin-top:12px;font-size:.875rem;color:#94a3b8}
              .badge{display:inline-block;background:#14532d;color:#86efac;font-size:.7rem;padding:3px 8px;border-radius:999px;margin-left:8px}
            </style></head><body>
            <div class="card">
              <div class="brand">payOS <span class="badge">MOCK ≈ LIVE</span></div>
              <h1>Thanh toán đơn hàng</h1>
              <p class="muted">OrderCode {{payment.OrderCode}}</p>
              <p class="muted">Payment {{payment.Id}}</p>
              <div class="amt">{{payment.Amount:N0}} {{payment.Currency}}</div>
              <p class="muted">Status: {{payment.Status}}</p>
              <button class="pay" onclick="pay()">Thanh toán thành công</button>
              <button class="cancel" onclick="cancel()">Hủy thanh toán</button>
              <div id="msg"></div>
            </div>
            <script>
              const returnUrl = {{JsonSerializer.Serialize(ret)}};
              const cancelUrl = {{JsonSerializer.Serialize(can)}};
              function appendPayOsParams(url, extra){
                if(!url) return null;
                const u = new URL(url, window.location.origin);
                Object.entries(extra).forEach(([k,v]) => u.searchParams.set(k, v));
                return u.toString();
              }
              async function pay(){
                const r = await fetch('/api/v1/payment/mock-checkout/{{payment.Id}}/pay',{method:'POST'});
                const j = await r.json();
                if(!r.ok){ document.getElementById('msg').textContent = JSON.stringify(j); return; }
                const next = appendPayOsParams(returnUrl, {
                  code: '00',
                  status: 'PAID',
                  orderCode: String(j.orderCode || '{{payment.OrderCode}}'),
                  cancel: 'false',
                  id: j.transactionReference || j.id || ''
                });
                document.getElementById('msg').textContent = next ? 'PAID — chuyển về returnUrl…' : 'PAID — đăng ký → pending_approval. Gọi POST /payment/{id}/sync.';
                if(next) setTimeout(() => location.href = next, 600);
              }
              async function cancel(){
                const r = await fetch('/api/v1/payment/mock-checkout/{{payment.Id}}/cancel',{method:'POST'});
                if(!r.ok){ document.getElementById('msg').textContent = 'Cancel failed'; return; }
                const next = appendPayOsParams(cancelUrl, {
                  code: '01',
                  status: 'CANCELLED',
                  orderCode: '{{payment.OrderCode}}',
                  cancel: 'true'
                });
                document.getElementById('msg').textContent = next ? 'Cancelled — chuyển về cancelUrl…' : 'Cancelled';
                if(next) setTimeout(() => location.href = next, 600);
              }
            </script>
            </body></html>
            """;
    }

    private sealed record CreateCheckoutRequest(Guid RegistrationId, string ReturnUrl, string CancelUrl);
    private sealed record CreatePackageCheckoutRequest(Guid PackagePurchaseId, string ReturnUrl, string CancelUrl);
}
