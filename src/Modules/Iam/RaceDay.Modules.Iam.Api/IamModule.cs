using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Modules.Iam.Application.Users.RegisterUser;
using RaceDay.Modules.Iam.Infrastructure;
using RaceDay.Shared.Web.Auth;

namespace RaceDay.Modules.Iam.Api;

public static class IamModule
{
    public static IServiceCollection AddIamModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(RegisterUserCommand).Assembly));
        services.AddValidatorsFromAssembly(typeof(RegisterUserCommand).Assembly);
        services.AddIamInfrastructure(configuration);
        return services;
    }

    public static IEndpointRouteBuilder MapIamEndpoints(this IEndpointRouteBuilder app)
    {
        var publicApi = app.MapGroup("/api/v1/iam")
            .RequireRateLimiting("public");

        publicApi.MapPost("/auth/register", async (RegisterUserCommand command, MediatR.IMediator mediator, CancellationToken ct) =>
        {
            var userId = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/iam/users/{userId}", new { userId });
        });

        var adminApi = app.MapGroup("/admin/v1/iam")
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .RequireRateLimiting("admin");

        adminApi.MapGet("/health", () => Results.Ok(new { module = "iam", status = "ok" }));

        return app;
    }
}
