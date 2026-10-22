using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Modules.Iam.Application.Abstractions;
using RaceDay.Modules.Iam.Domain.Users;

namespace RaceDay.Modules.Iam.Api;

/// <summary>Refresh human account claims only after JWT signature/lifetime validation.</summary>
internal static class CurrentAccountValidation
{
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        // Wearable tokens have their own device authentication and scoped policies.
        if (principal?.HasClaim("token_use", "wear") == true)
        {
            return;
        }

        var subject = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal?.FindFirstValue("sub");
        if (!Guid.TryParse(subject, out var userId))
        {
            context.Fail("Invalid account identifier.");
            return;
        }

        var services = context.HttpContext.RequestServices;
        var users = services.GetRequiredService<IUserRepository>();
        var roles = services.GetRequiredService<IRoleRepository>();
        var ct = context.HttpContext.RequestAborted;
        var user = await users.GetByIdAsync(userId, ct);
        if (user is null || user.Status != UserStatus.Active || !user.EmailVerified)
        {
            context.Fail("Account is unavailable.");
            return;
        }

        var role = await roles.GetByIdAsync(user.RoleId, ct);
        if (role is null || principal?.Identity is not ClaimsIdentity identity)
        {
            context.Fail("Account role is unavailable.");
            return;
        }

        // A previously issued Admin JWT must not retain access after a role change.
        foreach (var claimsIdentity in principal.Identities)
        {
            foreach (var claim in claimsIdentity.Claims
                         .Where(c => c.Type == ClaimTypes.Role || c.Type == "role").ToArray())
            {
                claimsIdentity.RemoveClaim(claim);
            }
        }
        identity.AddClaim(new Claim(ClaimTypes.Role, role.RoleName));
        identity.AddClaim(new Claim("role", role.RoleName));
    }
}
