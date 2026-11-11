using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Shared.Domain.Exceptions;
using RaceDay.Modules.Event.Contracts.Simulation;
using RaceDay.Modules.Simulation.Api;
using RaceDay.Modules.Simulation.Application;
using RaceDay.Modules.Simulation.Contracts;
using RaceDay.Modules.Simulation.Domain;
using RaceDay.Shared.Web;

namespace RaceDay.Modules.Simulation.UnitTests;

public class SimulationHubTests
{
    [Fact]
    public async Task Staff_stream_uses_authenticated_identity_and_stops_after_revocation()
    {
        var staffId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var distanceId = Guid.NewGuid();
        var signingKey = new string('k', 48);
        var workspace = new RevocableWorkspace(staffId, eventId, distanceId);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration["Jwt:SigningKey"] = signingKey;
        builder.Configuration["Jwt:Issuer"] = "raceday";
        builder.Configuration["Jwt:Audience"] = "raceday-clients";
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSharedWeb(builder.Configuration);
        builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, SimulationModule.UseHubAccessToken);
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<ISimulationFieldSource>(new FixedField(Field(eventId, distanceId, Guid.NewGuid())));
        builder.Services.AddSingleton<ISimulationCommitGateway, RecordingGateway>();
        builder.Services.AddSingleton<ISimulationBroadcaster, NoopSimulationBroadcaster>();
        builder.Services.AddSingleton<SimulationSessionBook>();
        builder.Services.AddSingleton<ISimulationAuthorizer, DenyAuthorizer>();
        builder.Services.AddSingleton<IStaffCheckpointWorkspace>(workspace);
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHub<SimulationHub>(SimulationModule.HubPath);
        await app.StartAsync();
        try
        {
            await using var connection = new HubConnectionBuilder()
                .WithUrl(app.Urls.Single() + SimulationModule.HubPath,
                    options => options.AccessTokenProvider = () => Task.FromResult<string?>(Token(signingKey, staffId)))
                .Build();
            await connection.StartAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var stream = await connection.StreamAsChannelAsync<StaffCheckpointSnapshotDto>("WatchCheckpoint", eventId, timeout.Token);
            var snapshot = await stream.ReadAsync(timeout.Token);
            Assert.Equal("cp", snapshot.Checkpoint.Id);
            Assert.Null(snapshot.RunId);
            workspace.Revoked = true;
            await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(async () =>
            {
                await foreach (var _ in stream.ReadAllAsync(timeout.Token)) { }
            });
            var run = await app.Services.GetRequiredService<SimulationSessionBook>().CreateAsync(eventId, distanceId, null, 1);
            await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(() => connection.InvokeAsync("JoinRun", run.RunId));
        }
        finally { await app.StopAsync(); }
    }

    private sealed class RevocableWorkspace(Guid staffId, Guid eventId, Guid distanceId) : IStaffCheckpointWorkspace
    {
        public volatile bool Revoked;
        public Task<StaffCheckpointSnapshotDto> ReadAsync(Guid id, Guid actor, CancellationToken ct = default)
        {
            if (Revoked || actor != staffId || id != eventId) throw new DomainException("Assignment revoked");
            return Task.FromResult(new StaffCheckpointSnapshotDto(eventId, "Race", distanceId, "Distance",
                new("cp", "Checkpoint", 1, 10, 106, "cp"), [], 2, null, 0, 0, null, 0, 1, null, [], []));
        }
    }

    [Fact]
    public async Task Join_checks_permission_and_reconnect_receives_the_current_snapshot()
    {
        var userId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var eventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var distanceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var athleteId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
        var signingKey = new string('k', 48);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration["Jwt:SigningKey"] = signingKey;
        builder.Configuration["Jwt:Issuer"] = "raceday";
        builder.Configuration["Jwt:Audience"] = "raceday-clients";
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSharedWeb(builder.Configuration);
        builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, SimulationModule.UseHubAccessToken);
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<ISimulationFieldSource>(new FixedField(Field(eventId, distanceId, athleteId)));
        builder.Services.AddSingleton<ISimulationCommitGateway, RecordingGateway>();
        builder.Services.AddSingleton<ISimulationBroadcaster, SignalRSimulationBroadcaster>();
        builder.Services.AddSingleton<SimulationSessionBook>();
        builder.Services.AddSingleton<IEventOrganizerAccessQuery>(new AllowUser(userId));
        builder.Services.AddScoped<ISimulationAuthorizer, SimulationAuthorizer>();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHub<SimulationHub>(SimulationModule.HubPath, options =>
        {
            options.LongPolling.PollTimeout = TimeSpan.FromSeconds(15);
        });
        await app.StartAsync();
        try
        {
            var book = app.Services.GetRequiredService<SimulationSessionBook>();
            var created = await book.CreateAsync(eventId, distanceId, null, 11);
            var hub = app.Urls.Single() + SimulationModule.HubPath;

            var snapshots = new List<SimulationSnapshotDto>();
            var connection = new HubConnectionBuilder()
                .WithUrl(hub, options => options.AccessTokenProvider = () => Task.FromResult<string?>(Token(signingKey, userId)))
                .Build();
            connection.On<SimulationSnapshotDto>("Snapshot", snapshots.Add);
            await connection.StartAsync();
            await connection.InvokeAsync("JoinRun", created.RunId);
            await WaitAsync(() => snapshots.Any(snapshot => snapshot.Version == 1));

            await book.StartAsync(created.RunId);
            await WaitAsync(() => snapshots.Any(snapshot => snapshot.Version >= 2 && snapshot.RunId == created.RunId));
            var latestBeforeDrop = snapshots.Where(snapshot => snapshot.RunId == created.RunId).Max(snapshot => snapshot.Version);

            await connection.StopAsync();
            snapshots.Clear();
            await connection.StartAsync();
            await connection.InvokeAsync("JoinRun", created.RunId);
            await WaitAsync(() => snapshots.Any(snapshot => snapshot.RunId == created.RunId && snapshot.Version == latestBeforeDrop));
            var rejoined = snapshots.Last(snapshot => snapshot.RunId == created.RunId);
            Assert.Equal(created.RunId, rejoined.RunId);
            Assert.Equal(1, rejoined.Generation);
            Assert.Equal(latestBeforeDrop, rejoined.Version);
            await connection.InvokeAsync("LeaveRun", created.RunId);
            await connection.StopAsync();

            var outsider = new HubConnectionBuilder()
                .WithUrl(hub, options => options.AccessTokenProvider = () => Task.FromResult<string?>(Token(signingKey, Guid.NewGuid())))
                .Build();
            await outsider.StartAsync();
            await Assert.ThrowsAnyAsync<Exception>(() => outsider.InvokeAsync("JoinRun", created.RunId));
            await outsider.StopAsync();

            var anonymous = new HubConnectionBuilder().WithUrl(hub).Build();
            await Assert.ThrowsAnyAsync<Exception>(() => anonymous.StartAsync());
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Join_reads_a_pause_that_happens_while_joining_the_group()
    {
        var eventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var distanceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var athleteId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
        var book = new SimulationSessionBook(
            new FixedField(Field(eventId, distanceId, athleteId)),
            new RecordingGateway(),
            new NoopSimulationBroadcaster());
        var created = await book.CreateAsync(eventId, distanceId, null, 11);
        var started = await book.StartAsync(created.RunId);

        var snapshot = await SimulationHubJoin.JoinAsync(
            book,
            new AllowAll(),
            created.RunId,
            () => book.PauseAsync(created.RunId),
            () => Task.CompletedTask);

        Assert.Equal(created.RunId, snapshot.RunId);
        Assert.Equal(started.Generation, snapshot.Generation);
        Assert.Equal("Paused", snapshot.Phase);
        Assert.True(snapshot.Version > started.Version);
    }

    [Fact]
    public async Task Join_checks_permission_before_the_group_and_drops_a_replaced_run()
    {
        var eventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var distanceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var athleteId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
        var book = new SimulationSessionBook(
            new FixedField(Field(eventId, distanceId, athleteId)),
            new RecordingGateway(),
            new NoopSimulationBroadcaster());
        var created = await book.CreateAsync(eventId, distanceId, null, 11);

        var joined = false;
        await Assert.ThrowsAsync<DomainException>(() => SimulationHubJoin.JoinAsync(
            book,
            new DenyAuthorizer(),
            created.RunId,
            () =>
            {
                joined = true;
                return Task.CompletedTask;
            },
            () => Task.CompletedTask));
        Assert.False(joined);

        var removed = false;
        var error = await Assert.ThrowsAsync<DomainException>(() => SimulationHubJoin.JoinAsync(
            book,
            new AllowAll(),
            created.RunId,
            () => book.ResetAsync(created.RunId, false),
            () =>
            {
                removed = true;
                return Task.CompletedTask;
            }));
        Assert.True(removed);
        Assert.Equal(SimulationHubJoin.ReplacedMessage, error.Message);
    }

    [Fact]
    public void Routes_match_the_demo_contract()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSignalR();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<ISimulationFieldSource>(new FixedField(Field(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"))));
        builder.Services.AddSingleton<ISimulationCommitGateway, RecordingGateway>();
        builder.Services.AddSingleton<ISimulationBroadcaster, NoopSimulationBroadcaster>();
        builder.Services.AddSingleton<SimulationSessionBook>();
        builder.Services.AddSingleton<ISimulationAuthorizer, AllowAll>();
        var app = builder.Build();
        app.MapSimulationEndpoints();
        var patterns = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? "")
            .ToList();

        string[] expected =
        [
            "/api/v1/simulation/events/{eventId:guid}/distances/{distanceId:guid}",
            "/api/v1/simulation/events/{eventId:guid}/distances/{distanceId:guid}/runs",
            "/api/v1/simulation/runs/{runId:guid}",
            "/api/v1/simulation/runs/{runId:guid}/start",
            "/api/v1/simulation/runs/{runId:guid}/pause",
            "/api/v1/simulation/runs/{runId:guid}/resume",
            "/api/v1/simulation/runs/{runId:guid}/playback",
            "/api/v1/simulation/runs/{runId:guid}/reset",
            "/api/v1/simulation/runs/{runId:guid}/new",
            "/api/v1/simulation/runs/{runId:guid}/commit",
            "/api/v1/simulation/runs/{runId:guid}/athletes/{registrationId:guid}/speed",
            "/api/v1/simulation/runs/{runId:guid}/athletes/{registrationId:guid}/sos",
            "/api/v1/simulation/runs/{runId:guid}/athletes/{registrationId:guid}/dnf",
            "/api/v1/simulation/runs/{runId:guid}/athletes/{registrationId:guid}/fraud/chip",
            "/api/v1/simulation/runs/{runId:guid}/athletes/{registrationId:guid}/fraud/passage",
            "/api/v1/simulation/hub"
        ];
        foreach (var route in expected)
        {
            Assert.Contains(patterns, pattern => pattern.Contains(route, StringComparison.Ordinal));
        }
    }

    private static async Task WaitAsync(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!ready() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(ready());
    }

    private static string Token(string signingKey, Guid userId, string? tokenUse = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Role, "Organizer")
        };
        if (tokenUse is not null)
        {
            claims.Add(new Claim("token_use", tokenUse));
        }

        var jwt = new JwtSecurityToken(
            "raceday",
            "raceday-clients",
            claims,
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static SimulationField Field(Guid eventId, Guid distanceId, Guid athleteId) => new()
    {
        EventId = eventId,
        DistanceId = distanceId,
        Title = "Demo",
        DistanceKm = 0.05,
        Polyline = [new SimulationCoursePoint(0, 0), new SimulationCoursePoint(1, 1)],
        Checkpoints =
        [
            new SimulationCourseCheckpoint("start", "Start", 0, 0, 0, "start"),
            new SimulationCourseCheckpoint("finish", "Finish", 0.05, 1, 1, "finish")
        ],
        Eligible = [new SimulationAthleteSeed(athleteId, "A001", "An", "chip-a")]
    };

    private sealed class FixedField(SimulationField field) : ISimulationFieldSource
    {
        public Task<SimulationField> LoadAsync(Guid eventId, Guid distanceId, CancellationToken cancellationToken = default)
            => Task.FromResult(field);
    }

    private sealed class RecordingGateway : ISimulationCommitGateway
    {
        public Task<SimulationCommitBatch> CommitAsync(SimulationCommitCommand command, CancellationToken cancellationToken = default)
            => Task.FromResult(new SimulationCommitBatch(false, "unused", []));
    }

    private sealed class AllowUser(Guid userId) : IEventOrganizerAccessQuery
    {
        public Task<EventOrganizerAccessInfo?> GetAsync(Guid eventId, CancellationToken cancellationToken = default)
            => Task.FromResult<EventOrganizerAccessInfo?>(new(eventId, userId, "Draft", "Demo"));
    }

    private sealed class AllowAll : ISimulationAuthorizer
    {
        public Task EnsureCanManageAsync(Guid eventId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class DenyAuthorizer : ISimulationAuthorizer
    {
        public Task EnsureCanManageAsync(Guid eventId, CancellationToken cancellationToken = default)
            => throw new DomainException("Bạn không có quyền điều khiển giả lập của giải này.");
    }
}
