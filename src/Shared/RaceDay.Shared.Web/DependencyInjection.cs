using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Application.Behaviors;
using RaceDay.Shared.Web.Auth;
using RaceDay.Shared.Web.Middleware;
using MediatR;

namespace RaceDay.Shared.Web;

public static class DependencyInjection
{
    public static IServiceCollection AddSharedWeb(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserAccessor, HttpCurrentUserAccessor>();
        services.AddJwtAuthentication(configuration);

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.PublicUser, policy => policy.RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.AdminOnly, policy =>
                policy.RequireAuthenticatedUser().RequireRole("Admin"));

        services.AddCors(options =>
        {
            options.AddPolicy("fe-dev", policy =>
            {
                policy
                    .WithOrigins(
                        "http://localhost:3000",
                        "http://localhost:3001",
                        "http://127.0.0.1:3000",
                        "http://127.0.0.1:3001")
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

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

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var signingKey = configuration["Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException("Jwt:SigningKey is missing. Set it via .env or configuration.");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidIssuer = configuration["Jwt:Issuer"] ?? "raceday",
                    ValidAudience = configuration["Jwt:Audience"] ?? "raceday-clients",
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role
                };
            });

        return services;
    }

    public static IApplicationBuilder UseSharedWeb(this IApplicationBuilder app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseCors("fe-dev");
        app.UseRateLimiter();
        return app;
    }
}
