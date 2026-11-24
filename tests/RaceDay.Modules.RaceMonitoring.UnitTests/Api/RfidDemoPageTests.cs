using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Api;

public class RfidDemoPageTests
{
    [Fact]
    public async Task Demo_Page_Is_Served_At_The_Documented_Path()
    {
        var wwwroot = FindWebRoot();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = ["--urls", "http://127.0.0.1:0"],
            WebRootPath = wwwroot,
            ContentRootPath = Path.GetTempPath()
        });
        var app = builder.Build();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        using var client = new HttpClient();

        var directory = await client.GetAsync(address.TrimEnd('/') + "/rfid-demo/");
        var file = await client.GetAsync(address.TrimEnd('/') + "/rfid-demo/index.html");
        var html = await directory.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, directory.StatusCode);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Contains("text/html", directory.Content.Headers.ContentType?.MediaType);
        Assert.Contains("RFID Demo", html);
        Assert.Contains("Bắt đầu quét checkpoint", html);
        Assert.Contains("/api/v1/iam/auth/login", html);
        Assert.Contains("/api/v1/event/organizer/mine", html);
        Assert.Contains("/api/v1/race-monitoring/rfid/read-requests", html);
        Assert.Contains("/api/v1/race-monitoring/rfid/assignments", html);
        Assert.Contains("/api/v1/race-monitoring/events/", html);
        Assert.Contains("race-logs?bibNumber=", html);
        Assert.Contains("eventId", html);
        Assert.Contains("registrationId", html);
        Assert.DoesNotContain("X-Service-Key", html);
        Assert.DoesNotContain("serviceKey", html);
        Assert.Contains("UseDefaultFiles", await File.ReadAllTextAsync(FindProgram()));
        await app.StopAsync();
        await app.DisposeAsync();
    }

    private static string FindWebRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Hosts", "RaceDay.Api", "wwwroot");
            if (File.Exists(Path.Combine(candidate, "rfid-demo", "index.html")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Không thấy wwwroot của RaceDay.Api.");
    }

    private static string FindProgram()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Hosts", "RaceDay.Api", "Program.cs");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Không thấy Program.cs của RaceDay.Api.");
    }
}
