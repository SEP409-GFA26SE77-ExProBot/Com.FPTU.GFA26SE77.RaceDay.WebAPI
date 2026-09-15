using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace RaceDay.Modules.Template.Api;

/// <summary>
/// Copy Modules/_Template → Modules/YourModule, rename namespaces, then register in Host.
/// </summary>
public static class TemplateModule
{
    public static IServiceCollection AddTemplateModule(this IServiceCollection services, IConfiguration configuration)
    {
        _ = configuration;
        return services;
    }

    public static IEndpointRouteBuilder MapTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/_template/health", () => Results.Ok(new { module = "_template", status = "scaffold" }));
        return app;
    }
}
