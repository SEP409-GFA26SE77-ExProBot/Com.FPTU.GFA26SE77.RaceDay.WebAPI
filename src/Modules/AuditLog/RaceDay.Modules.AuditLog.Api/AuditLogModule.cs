using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Modules.AuditLog.Application.AuditEntries;
using RaceDay.Modules.AuditLog.Application.EventHandlers;
using RaceDay.Modules.AuditLog.Infrastructure;
using RaceDay.Shared.Web.Auth;
using RaceDay.Shared.Web.OpenApi;

namespace RaceDay.Modules.AuditLog.Api;

public static class AuditLogModule
{
    public static IServiceCollection AddAuditLogModule(this IServiceCollection services, IConfiguration configuration)
    {
        // MediatR đăng ký tập trung ở Host (Program.cs)
        services.AddAuditLogInfrastructure(configuration);
        return services;
    }

    public static IEndpointRouteBuilder MapAuditLogEndpoints(this IEndpointRouteBuilder app)
    {
        var adminApi = app.MapGroup("/api/v1/audit-logs")
            .WithTags(ApiTags.AuditLog)
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .RequireRateLimiting("admin");

        adminApi.MapGet("/", async (int? take, IMediator mediator, CancellationToken ct) =>
        {
            var entries = await mediator.Send(new ListRecentAuditEntriesQuery(take ?? 50), ct);
            return Results.Ok(entries);
        })
        .WithSummary("Danh sách audit gần đây (Admin)");

        return app;
    }
}
