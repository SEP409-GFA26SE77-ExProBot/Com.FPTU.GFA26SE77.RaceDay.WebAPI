using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Modules.Simulation.Application;
using RaceDay.Modules.Simulation.Contracts;
using RaceDay.Modules.Simulation.Infrastructure;
using RaceDay.Shared.Domain.Exceptions;
using RaceDay.Shared.Web.Auth;
using RaceDay.Shared.Web.OpenApi;

namespace RaceDay.Modules.Simulation.Api;

public static class SimulationModule
{
    public const string HubPath = "/api/v1/simulation/hub";

    public static IServiceCollection AddSimulationModule(this IServiceCollection services, IConfiguration configuration)
    {
        _ = configuration;
        services.AddSignalR();
        services.AddSingleton<ISimulationFieldSource, ScopedSimulationFieldSource>();
        services.AddSingleton<ISimulationCommitGateway, ScopedSimulationCommitGateway>();
        services.AddSingleton<ISimulationBroadcaster, SignalRSimulationBroadcaster>();
        services.AddSingleton<SimulationSessionBook>();
        services.AddScoped<ISimulationAuthorizer, SimulationAuthorizer>();
        services.AddScoped<IStaffCheckpointWorkspace, StaffCheckpointWorkspace>();
        services.AddHostedService<SimulationClockHostedService>();
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, UseHubAccessToken);
        return services;
    }

    public static void UseHubAccessToken(JwtBearerOptions options)
    {
        var previous = options.Events?.OnMessageReceived;
        options.Events ??= new JwtBearerEvents();
        options.Events.OnMessageReceived = async context =>
        {
            if (previous is not null)
            {
                await previous(context);
            }

            if (!string.IsNullOrEmpty(context.Token))
            {
                return;
            }

            var path = context.HttpContext.Request.Path;
            if (path.StartsWithSegments(HubPath)
                && context.Request.Query.TryGetValue("access_token", out var token)
                && !string.IsNullOrWhiteSpace(token))
            {
                context.Token = token;
            }
        };
    }

    public static IEndpointRouteBuilder MapSimulationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHub<SimulationHub>(HubPath, options =>
        {
            options.LongPolling.PollTimeout = TimeSpan.FromSeconds(15);
        });

        var api = app.MapGroup("/api/v1/simulation")
            .WithTags(ApiTags.Simulation)
            .RequireAuthorization(AuthPolicies.HumanUser);

        api.MapStaffSimulationEndpoints();

        api.MapGet("/events/{eventId:guid}/distances/{distanceId:guid}", async (
            Guid eventId,
            Guid distanceId,
            SimulationSessionBook book,
            ISimulationAuthorizer authorizer,
            CancellationToken ct) =>
        {
            await authorizer.EnsureCanManageAsync(eventId, ct);
            return Results.Ok(await book.PreviewAsync(eventId, distanceId, ct));
        }).WithSummary("Đọc course, số VĐV hợp lệ và activeRunId của lượt hiện tại. Không tạo lượt.");

        api.MapPost("/events/{eventId:guid}/distances/{distanceId:guid}/runs", async (
            Guid eventId,
            Guid distanceId,
            HttpRequest request,
            SimulationSessionBook book,
            ISimulationAuthorizer authorizer,
            CancellationToken ct) =>
        {
            await authorizer.EnsureCanManageAsync(eventId, ct);
            var body = await ReadOptional<CreateRunBody>(request, ct);
            return Results.Ok(await book.CreateAsync(eventId, distanceId, body?.RunnerCount, body?.Seed, ct));
        }).WithSummary("Tạo lượt nháp, hoặc trả lượt đang có của cùng event và cự ly.");

        api.MapGet("/runs/{runId:guid}", async (
            Guid runId,
            SimulationSessionBook book,
            ISimulationAuthorizer authorizer,
            CancellationToken ct) =>
            Results.Ok(await SimulationAccess.ReadAsync(book, authorizer, runId, ct)))
            .WithSummary("Đọc snapshot mới nhất. Dùng sau reconnect.");

        api.MapPost("/runs/{runId:guid}/start", (Guid runId, SimulationSessionBook book, ISimulationAuthorizer authorizer, CancellationToken ct) =>
            Mutate(runId, book, authorizer, () => book.StartAsync(runId, ct), ct));
        api.MapPost("/runs/{runId:guid}/pause", (Guid runId, SimulationSessionBook book, ISimulationAuthorizer authorizer, CancellationToken ct) =>
            Mutate(runId, book, authorizer, () => book.PauseAsync(runId, ct), ct));
        api.MapPost("/runs/{runId:guid}/resume", (Guid runId, SimulationSessionBook book, ISimulationAuthorizer authorizer, CancellationToken ct) =>
            Mutate(runId, book, authorizer, () => book.ResumeAsync(runId, ct), ct));
        api.MapPost("/runs/{runId:guid}/playback", async (
            Guid runId,
            HttpRequest request,
            SimulationSessionBook book,
            ISimulationAuthorizer authorizer,
            CancellationToken ct) =>
        {
            var body = await ReadOptional<PlaybackBody>(request, ct) ?? throw new DomainException("Thiếu tốc độ phát.");
            return await Mutate(runId, book, authorizer, () => book.SetPlaybackAsync(runId, body.Rate, ct), ct);
        });
        api.MapPost("/runs/{runId:guid}/reset", async (
            Guid runId,
            HttpRequest request,
            SimulationSessionBook book,
            ISimulationAuthorizer authorizer,
            CancellationToken ct) =>
        {
            var body = await ReadOptional<RunControlBody>(request, ct);
            return await Mutate(runId, book, authorizer, () => book.ResetAsync(runId, body?.ConfirmDiscard ?? false, ct), ct);
        });
        api.MapPost("/runs/{runId:guid}/new", async (
            Guid runId,
            HttpRequest request,
            SimulationSessionBook book,
            ISimulationAuthorizer authorizer,
            CancellationToken ct) =>
        {
            var body = await ReadOptional<RunControlBody>(request, ct);
            return await Mutate(runId, book, authorizer,
                () => book.NewRunAsync(runId, body?.ConfirmDiscard ?? false, body?.Seed, body?.RunnerCount, ct), ct);
        });
        api.MapPost("/runs/{runId:guid}/commit", (
            Guid runId,
            SimulationSessionBook book,
            ISimulationAuthorizer authorizer,
            CancellationToken ct) => Commit(runId, book, authorizer, ct));

        api.MapPost("/runs/{runId:guid}/athletes/{registrationId:guid}/speed", async (
            Guid runId,
            Guid registrationId,
            HttpRequest request,
            SimulationSessionBook book,
            ISimulationAuthorizer authorizer,
            CancellationToken ct) =>
        {
            var body = await ReadOptional<SpeedBody>(request, ct) ?? throw new DomainException("Thiếu tốc độ.");
            return await Mutate(runId, book, authorizer, () => book.SetBaseSpeedAsync(runId, registrationId, body.Kmh, ct), ct);
        });
        api.MapPost("/runs/{runId:guid}/athletes/{registrationId:guid}/sos", (
            Guid runId, Guid registrationId, SimulationSessionBook book, ISimulationAuthorizer authorizer, CancellationToken ct) =>
            Mutate(runId, book, authorizer, () => book.SosAsync(runId, registrationId, ct), ct));
        api.MapPost("/runs/{runId:guid}/athletes/{registrationId:guid}/fraud/chip", async (
            Guid runId,
            Guid registrationId,
            HttpRequest request,
            SimulationSessionBook book,
            ISimulationAuthorizer authorizer,
            CancellationToken ct) =>
        {
            var body = await ReadOptional<ChipBody>(request, ct) ?? throw new DomainException("Thiếu chip.");
            return await Mutate(runId, book, authorizer, () => book.AssignChipAsync(runId, registrationId, body.ChipId, ct), ct);
        });
        api.MapPost("/runs/{runId:guid}/athletes/{registrationId:guid}/fraud/passage", async (
            Guid runId,
            Guid registrationId,
            HttpRequest request,
            SimulationSessionBook book,
            ISimulationAuthorizer authorizer,
            CancellationToken ct) =>
        {
            var body = await ReadOptional<PassageBody>(request, ct) ?? throw new DomainException("Thiếu checkpoint.");
            return await Mutate(runId, book, authorizer, () => book.InjectPassageAsync(runId, registrationId, body.CheckpointId, ct), ct);
        });

        return app;
    }

    private static async Task<IResult> Mutate(
        Guid runId,
        SimulationSessionBook book,
        ISimulationAuthorizer authorizer,
        Func<Task<SimulationSnapshotDto>> action,
        CancellationToken cancellationToken)
    {
        var eventId = book.EventIdOf(runId) ?? throw new DomainException("Không tìm thấy lượt giả lập.");
        await authorizer.EnsureCanManageAsync(eventId, cancellationToken);
        try
        {
            return Results.Ok(await action());
        }
        catch (SimulationBusyException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> Commit(
        Guid runId,
        SimulationSessionBook book,
        ISimulationAuthorizer authorizer,
        CancellationToken cancellationToken)
    {
        var eventId = book.EventIdOf(runId) ?? throw new DomainException("Không tìm thấy lượt giả lập.");
        await authorizer.EnsureCanManageAsync(eventId, cancellationToken);
        try
        {
            return Results.Ok(await book.CommitAsync(runId, cancellationToken));
        }
        catch (SimulationBusyException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<T?> ReadOptional<T>(HttpRequest request, CancellationToken cancellationToken)
        where T : class
    {
        if (request.ContentLength is 0)
        {
            return null;
        }

        try
        {
            return await request.ReadFromJsonAsync<T>(cancellationToken);
        }
        catch (JsonException)
        {
            throw new DomainException("Body không hợp lệ.");
        }
    }

    private sealed record CreateRunBody(int? RunnerCount, int? Seed);

    private sealed record PlaybackBody(double Rate);

    private sealed record SpeedBody(double Kmh);

    private sealed record ChipBody(string ChipId);

    private sealed record PassageBody(string CheckpointId);

    private sealed record RunControlBody(bool ConfirmDiscard, int? Seed, int? RunnerCount);
}
