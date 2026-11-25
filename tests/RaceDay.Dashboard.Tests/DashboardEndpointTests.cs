using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RaceDay.Modules.Event.Api;
using RaceDay.Modules.Event.Application.Dashboard;
using RaceDay.Modules.Finance.Api;
using RaceDay.Modules.Finance.Application.Dashboard;
using RaceDay.Modules.Iam.Api;
using RaceDay.Modules.Iam.Application.Dashboard;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Web;
using RaceDay.Shared.Web.Auth;

namespace RaceDay.Dashboard.Tests;

public class DashboardEndpointTests
{
    [Fact]
    public async Task AllFiveRoutes_BindParameters_AndRequireExpectedPolicies()
    {
        var mediator = Substitute.For<IMediator>();
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Jwt:SigningKey"] = "dashboard-test-only-signing-key-1234567890";
        builder.Services.AddSharedWeb(builder.Configuration);
        builder.Services.AddSingleton(mediator);
        builder.Services.AddSingleton(Substitute.For<IFileStorage>());
        builder.Services.AddSingleton(Substitute.For<RaceDay.Modules.Event.Application.Abstractions.IRaceEventRepository>());
        builder.Services.AddSingleton(Substitute.For<RaceDay.Modules.Event.Infrastructure.Events.ILeaderboardPdfService>());
        await using var app = builder.Build();
        app.MapIamEndpoints();
        app.MapEventEndpoints();
        app.MapFinanceEndpoints();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(x => x.Endpoints).OfType<RouteEndpoint>().ToArray();
        var paths = new[] {
            "/api/v1/iam/admin/dashboard",
            "/api/v1/event/organizer/dashboard",
            "/api/v1/event/organizer/{eventId:guid}/dashboard",
            "/api/v1/event/organizer/{eventId:guid}/dashboard/registrations-trend",
            "/api/v1/finance/organizer/revenue-trend"
        };
        var eventId = Guid.NewGuid();
        var from = new DateOnly(2026, 11, 1);
        var to = new DateOnly(2026, 11, 15);
        foreach (var path in paths)
        {
            var endpoint = endpoints.Single(x => x.RoutePattern.RawText == path);
            Assert.Contains("GET", endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            Assert.Contains(policies, x => x.Policy == (path.Contains("/iam/") ? AuthPolicies.AdminOnly : AuthPolicies.PublicUser));
            if (!path.Contains("/iam/")) Assert.Contains(policies, x => x.Policy == AuthPolicies.HumanUser);
            var context = new DefaultHttpContext { RequestServices = app.Services };
            context.Request.Method = "GET";
            context.Request.RouteValues["eventId"] = eventId.ToString();
            context.Request.QueryString = new QueryString($"?eventId={eventId}&from=2026-11-01&to=2026-11-15");
            context.Response.Body = new MemoryStream();
            await endpoint.RequestDelegate!(context);
            Assert.Equal(200, context.Response.StatusCode);
        }
        await mediator.Received(1).Send(Arg.Is<GetAdminDashboardQuery>(q => q.From == from && q.To == to), Arg.Any<CancellationToken>());
        await mediator.Received(1).Send(Arg.Any<GetOrganizerDashboardQuery>(), Arg.Any<CancellationToken>());
        await mediator.Received(1).Send(Arg.Is<GetEventDashboardQuery>(q => q.EventId == eventId), Arg.Any<CancellationToken>());
        await mediator.Received(1).Send(Arg.Is<GetRegistrationTrendQuery>(q => q.EventId == eventId && q.From == from && q.To == to), Arg.Any<CancellationToken>());
        await mediator.Received(1).Send(Arg.Is<GetRevenueTrendQuery>(q => q.EventId == eventId && q.From == from && q.To == to), Arg.Any<CancellationToken>());

        var auth = app.Services.GetRequiredService<IAuthorizationService>();
        ClaimsPrincipal Principal(string role, bool wear = false) => new(new ClaimsIdentity(
            wear ? [new Claim(ClaimTypes.Role, role), new Claim("token_use", "wear")] : [new Claim(ClaimTypes.Role, role)], "test"));
        Assert.True((await auth.AuthorizeAsync(Principal("Admin"), null, AuthPolicies.AdminOnly)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(Principal("Runner"), null, AuthPolicies.AdminOnly)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(new ClaimsPrincipal(), null, AuthPolicies.PublicUser)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(Principal("Runner", true), null, AuthPolicies.HumanUser)).Succeeded);
    }
}
