using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Shared.Web.Auth;

public sealed class HttpCurrentUserAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentUserAccessor
{
    public Guid? UserId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? httpContextAccessor.HttpContext?.User?.FindFirstValue("sub");
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }
}
