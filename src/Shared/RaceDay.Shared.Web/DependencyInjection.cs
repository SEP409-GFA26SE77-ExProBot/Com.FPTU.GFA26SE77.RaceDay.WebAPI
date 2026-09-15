using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Shared.Web.Auth;
using RaceDay.Shared.Web.Middleware;

namespace RaceDay.Shared.Web;

public static class DependencyInjection
{
    public static IServiceCollection AddSharedWeb(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.PublicUser, policy => policy.RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.AdminOnly, policy =>
                policy.RequireAuthenticatedUser().RequireRole("Admin"));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter("public", limiter =>
            {
                limiter.PermitLimit = 100;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            });
            options.AddFixedWindowLimiter("admin", limiter =>
            {
                limiter.PermitLimit = 300;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            });
        });

        return services;
    }

    public static IApplicationBuilder UseSharedWeb(this IApplicationBuilder app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseRateLimiter();
        return app;
    }
}
