using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Modules.AuditLog.Application.Abstractions;
using RaceDay.Modules.AuditLog.Application.EventHandlers;
using RaceDay.Modules.AuditLog.Infrastructure;
using RaceDay.Shared.Web.Auth;

namespace RaceDay.Modules.AuditLog.Api;

public static class AuditLogModule
{
    public static IServiceCollection AddAuditLogModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(UserCreatedAuditHandler).Assembly));
        services.AddAuditLogInfrastructure(configuration);
        return services;
    }

    public static IEndpointRouteBuilder MapAuditLogEndpoints(this IEndpointRouteBuilder app)
    {
        var adminApi = app.MapGroup("/admin/v1/audit-logs")
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .RequireRateLimiting("admin");

        adminApi.MapGet("/", async (IAuditEntryRepository repository, CancellationToken ct) =>
        {
            var entries = await repository.ListRecentAsync(50, ct);
            return Results.Ok(entries.Select(x => new
            {
                x.Id,
                x.Action,
                x.ActorId,
                x.ResourceType,
                x.ResourceId,
                x.OccurredOnUtc
            }));
        });

        return app;
    }
}
