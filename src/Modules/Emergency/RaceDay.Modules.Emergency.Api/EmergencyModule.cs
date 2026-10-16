using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RaceDay.Modules.Emergency.Api;

public static class EmergencyModule
{
    public static IEndpointRouteBuilder MapEmergencyEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1/sos")
            .WithTags("Emergency - SOS");

        api.MapGet("/stream", async (HttpResponse response, CancellationToken ct) =>
        {
            response.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache";
            response.Headers["X-Accel-Buffering"] = "no";

            while (!ct.IsCancellationRequested)
            {
                await response.WriteAsync("data: SOS\n\n", ct);
                await response.Body.FlushAsync(ct);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        })
        .WithSummary("Stream SOS messages to the frontend")
        .WithDescription("Keeps an SSE connection open and sends the string 'SOS' every two seconds.");

        return app;
    }
}
