using System.Security.Claims;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RaceDay.Modules.Event.Api;
using RaceDay.Modules.Event.Application.Registrations;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Web;
using RaceDay.Shared.Web.Auth;
using RaceDay.Shared.Web.Middleware;

namespace RaceDay.Modules.Event.IntegrationTests;

public class RaceStatusEndpointTests
{
    [Fact]
    public async Task Patch_BindsRouteAndBody_AndRequiresHumanAuthentication()
    {
        var mediator = Substitute.For<IMediator>();
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Jwt:SigningKey"] = "race-status-test-only-signing-key-1234567890";
        builder.Services.AddSharedWeb(builder.Configuration);
        builder.Services.AddSingleton(mediator);
        builder.Services.AddSingleton(Substitute.For<IFileStorage>());
        await using var app = builder.Build();
        app.MapEventEndpoints();
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(x => x.Endpoints)
            .OfType<RouteEndpoint>().Single(x => x.RoutePattern.RawText == "/api/v1/event/{eventId:guid}/registrations/{registrationId:guid}/race-status");
        Assert.Contains("PATCH", endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
        var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        Assert.Contains(policies, x => x.Policy == AuthPolicies.HumanUser);
        Assert.Contains(policies, x => x.Policy == AuthPolicies.PublicUser);

        var auth = app.Services.GetRequiredService<IAuthorizationService>();
        var human = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test"));
        var wear = new ClaimsPrincipal(new ClaimsIdentity([new Claim("token_use", "wear")], "test"));
        Assert.True((await auth.AuthorizeAsync(human, null, AuthPolicies.HumanUser)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(wear, null, AuthPolicies.HumanUser)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(new ClaimsPrincipal(), null, AuthPolicies.HumanUser)).Succeeded);

        var eventId = Guid.NewGuid();
        var registrationId = Guid.NewGuid();
        var result = new RaceStatusChangeDto(registrationId, eventId, "1001", "running", "dnf",
            "Stopped at CP2", DateTime.UtcNow, Guid.NewGuid());
        mediator.Send(Arg.Any<ChangeRaceStatusCommand>(), Arg.Any<CancellationToken>()).Returns(result);
        var context = new DefaultHttpContext { RequestServices = app.Services, User = human };
        context.Request.Method = "PATCH";
        context.Request.RouteValues["eventId"] = eventId.ToString();
        context.Request.RouteValues["registrationId"] = registrationId.ToString();
        var body = Encoding.UTF8.GetBytes("""{"status":"dnf","reason":"Stopped at CP2"}""");
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = body.Length;
        context.Request.Body = new MemoryStream(body);
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyFeature());
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);
        Assert.Equal(200, context.Response.StatusCode);
        await mediator.Received(1).Send(Arg.Is<ChangeRaceStatusCommand>(x => x.EventId == eventId
            && x.RegistrationId == registrationId && x.Status == "dnf" && x.Reason == "Stopped at CP2"), Arg.Any<CancellationToken>());
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("dnf", json.RootElement.GetProperty("raceStatus").GetString());
        Assert.Equal(registrationId, json.RootElement.GetProperty("registrationId").GetGuid());
    }

    [Fact]
    public async Task ConcurrentWrite_Returns409WithReloadInstruction()
    {
        var environment = Substitute.For<IHostEnvironment>();
        var middleware = new ExceptionHandlingMiddleware(_ => throw new DbUpdateConcurrencyException(),
            NullLogger<ExceptionHandlingMiddleware>.Instance, environment);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context);
        Assert.Equal(409, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Contains("tải lại", json.RootElement.GetProperty("error").GetString());
    }

    private sealed class BodyFeature : IHttpRequestBodyDetectionFeature { public bool CanHaveBody => true; }
}
