using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Modules.RaceMonitoring.Application;
using RaceDay.Modules.RaceMonitoring.Infrastructure;
using RaceDay.Shared.Web.OpenApi;

namespace RaceDay.Modules.RaceMonitoring.Api;

public static class RaceMonitoringModule
{
    public static IServiceCollection AddRaceMonitoringModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(RaceMonitoringApplicationMarker).Assembly));
        services.AddValidatorsFromAssembly(typeof(RaceMonitoringApplicationMarker).Assembly);
        services.AddRaceMonitoringInfrastructure(configuration);
        return services;
    }

    public static IEndpointRouteBuilder MapRaceMonitoringEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1/race-monitoring")
            .WithTags(ApiTags.RaceMonitoring)
            .RequireRateLimiting("public");

        api.MapGet("/health", () => Results.Ok(new { module = "race-monitoring", status = "ready" }))
            .WithSummary("RaceMonitoring module health");

        return app;
    }
}
