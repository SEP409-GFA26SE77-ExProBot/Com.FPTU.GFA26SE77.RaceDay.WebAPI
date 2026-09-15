using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace RaceDay.Modules.Payment.Api;

/// <summary>
/// Skeleton only — implement later. Registered so Host wiring is ready.
/// </summary>
public static class PaymentModule
{
    public static IServiceCollection AddPaymentModule(this IServiceCollection services, IConfiguration configuration)
    {
        _ = configuration;
        return services;
    }

    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/payment/status", () =>
            Results.Ok(new { module = "payment", status = "not_implemented" }));

        return app;
    }
}
