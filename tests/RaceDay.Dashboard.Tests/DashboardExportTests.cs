using ClosedXML.Excel;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RaceDay.Api.Dashboard;
using RaceDay.Modules.Event.Application.Dashboard;
using RaceDay.Modules.Finance.Application.Dashboard;
using RaceDay.Modules.Finance.Application.Wallet;
using RaceDay.Modules.Iam.Application.Dashboard;
using RaceDay.Modules.AuditLog.Application.AuditEntries;
using RaceDay.Shared.Application.Dashboard;
using RaceDay.Shared.Domain.Exceptions;
using RaceDay.Shared.Web.Auth;

namespace RaceDay.Dashboard.Tests;

public class DashboardExportTests
{
    private static readonly DateOnly Day = new(2026, 11, 15);
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid DistanceId = Guid.NewGuid();
    private static readonly Guid OtherDistanceId = Guid.NewGuid();
    private static EventDashboardDto EventData() => new(EventId, 8,
        [new("confirmed", 3), new("finished", 2), new("pending_payment", 1), new("cancelled", 2)],
        new(5, 4), [new("finished", 1), new("dq", 1), new("running", 3)], 0,
        [new(DistanceId, "5 km", 0, 4, 3), new(OtherDistanceId, "10 km", 10, 2, 2)],
        new(2, 1, ["security"], 1, 1, 5, 3), DateTime.UtcNow);
    private static RegistrationTrendDto Trend() => new(EventId, Day, Day, DashboardDateRange.TimeZone, 8,
        [new(Day, 8)], [new(DistanceId, "5 km", [new(Day, 3)]), new(OtherDistanceId, "10 km", [new(Day, 5)])], DateTime.UtcNow);
    private static RevenueTrendDto Revenue() => new(EventId, Day, Day, DashboardDateRange.TimeZone,
        [new("VND", 100000, 150000, [new(Day, 100000, 150000)]), new("USD", 10.5m, 0, [new(Day, 10.5m, 0)])], DateTime.UtcNow);
    private static EventLedgerDto Finance() => new(EventId, "=Giải chạy thử tiếng Việt", "closed", 5,
        100000, 5, 5000, 150000, -55000, 0, 0, 0, 0, "Theo chính sách", "VND");

    [Fact]
    public void Report_PreservesGrouping_DistanceScope_Currencies_AndUnlimitedCapacity()
    {
        var report = DashboardReports.Event(EventData(), Trend(), Revenue(), Finance(), DistanceId);
        Assert.Equal(5, report.Sections.Single(x => x.Title == "Nhóm đăng ký").Rows.Single(x => (string)x[0] == "Đăng ký đã xác nhận")[1]);
        Assert.Equal(3, report.Sections.Single(x => x.Title == "Đăng ký mới").Rows.Single()[1]);
        Assert.Equal("Không giới hạn", report.Sections.Single(x => x.Title == "Sức chứa theo cự ly").Rows.First()[1]);
        var cash = report.Sections.Single(x => x.Title == "Dòng tiền trong kỳ");
        Assert.Equal(2, cash.Rows.Length);
        Assert.Equal(-50000m, cash.Rows[0][3]);
        Assert.Equal(10.5m, cash.Rows[1][1]);
        // DQ after finishing must not be counted as a race finisher.
        Assert.Equal(1, report.Sections.Single(x => x.Title == "Tình hình thi đấu").Rows.Single(x => (string)x[0] == "Đã hoàn thành")[1]);
    }

    [Fact]
    public void Excel_IsReadable_KeepsNumbersAndVietnamese_AndNeverEvaluatesExternalText()
    {
        var report = new DashboardReport("Dashboard", "Dữ liệu thử", Day, Day, DateTime.UtcNow, [
            new("Dữ liệu", "Số liệu hiện tại", ["Giải", "Giá trị"], [["=1+1", 12.5m], ["Đường chạy Việt Nam", -4]])]);
        using var stream = new MemoryStream(DashboardReportWriter.Excel(report));
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Dữ liệu");
        Assert.Equal("=1+1", sheet.Cell(9, 1).GetString());
        Assert.False(sheet.Cell(9, 1).HasFormula);
        Assert.Equal(12.5m, sheet.Cell(9, 2).GetValue<decimal>());
        Assert.Equal("Đường chạy Việt Nam", sheet.Cell(10, 1).GetString());
        Assert.Contains("UTC+7", sheet.Cell(3, 1).GetString());
    }

    [Fact]
    public void BothFormats_RenderEmptyData_And366DayReports_WithLongVietnameseText()
    {
        var admin = DashboardReports.Admin(new(0, 0, 0, 0, 0, [], [], Day, Day, DashboardDateRange.TimeZone, DateTime.UtcNow), []);
        var organizer = DashboardReports.Organizer(new(0, [], 0, 0, [], [], DateTime.UtcNow), new(0, 0, 0, 0, "VND", null, []), Revenue());
        var days = Enumerable.Range(0, 366).Select(i => new RevenueDayDto(Day.AddDays(i), 100000, 150000)).ToArray();
        var revenue = new RevenueTrendDto(EventId, Day, Day.AddDays(365), DashboardDateRange.TimeZone, [new("VND", 36600000, 54900000, days)], DateTime.UtcNow);
        var race = DashboardReports.Event(EventData(), Trend() with { To = revenue.To }, revenue,
            Finance() with { EventName = string.Concat(Enumerable.Repeat("Giải chạy cộng đồng Việt Nam ", 8)) }, null);
        foreach (var (name, report) in new[] { ("admin", admin), ("organizer", organizer), ("event", race) })
        {
            var pdf = DashboardReportWriter.Pdf(report);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
            var xlsx = DashboardReportWriter.Excel(report);
            using var stream = new MemoryStream(xlsx);
            using var workbook = new XLWorkbook(stream);
            Assert.Equal(report.Sections.Count, workbook.Worksheets.Count);
            // Optional local artifacts for visual QA; normal test runs do not write reports.
            if (Environment.GetEnvironmentVariable("RACEDAY_DASHBOARD_QA_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name + ".pdf"), pdf);
                File.WriteAllBytes(Path.Combine(directory, name + ".xlsx"), xlsx);
            }
        }
    }

    [Fact]
    public async Task ExportRoutes_RequirePolicies_ValidateFormatAndDistance_BeforeReadingOtherData()
    {
        var builder = WebApplication.CreateBuilder();
        var mediator = Substitute.For<IMediator>();
        builder.Services.AddSingleton(mediator);
        await using var app = builder.Build();
        app.MapDashboardExportEndpoints();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(x => x.Endpoints).OfType<RouteEndpoint>().ToArray();
        Assert.Equal(3, endpoints.Length);
        foreach (var endpoint in endpoints)
        {
            var admin = endpoint.RoutePattern.RawText!.Contains("/iam/");
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            Assert.Contains(policies, x => x.Policy == (admin ? AuthPolicies.AdminOnly : AuthPolicies.PublicUser));
            if (!admin) Assert.Contains(policies, x => x.Policy == AuthPolicies.HumanUser);
            var context = Context(app.Services, "?format=csv");
            await Assert.ThrowsAsync<DomainException>(() => endpoint.RequestDelegate!(context));
        }
        await mediator.DidNotReceive().Send(Arg.Any<GetAdminDashboardQuery>(), Arg.Any<CancellationToken>());
        var eventEndpoint = endpoints.Single(x => x.RoutePattern.RawText!.Contains("{eventId:guid}"));
        mediator.Send(Arg.Any<GetEventDashboardQuery>(), Arg.Any<CancellationToken>()).Returns(EventData());
        await Assert.ThrowsAsync<DomainException>(() => eventEndpoint.RequestDelegate!(Context(app.Services, $"?format=xlsx&distanceId={Guid.NewGuid()}")));
        await mediator.DidNotReceive().Send(Arg.Any<GetRevenueTrendQuery>(), Arg.Any<CancellationToken>());
        mediator.Send(Arg.Any<GetEventDashboardQuery>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<EventDashboardDto>(new DomainException("Không tìm thấy giải thuộc quyền quản lý của bạn.")));
        await Assert.ThrowsAsync<DomainException>(() => eventEndpoint.RequestDelegate!(Context(app.Services, "?format=pdf")));
        await mediator.DidNotReceive().Send(Arg.Any<GetEventFinanceSummaryQuery>(), Arg.Any<CancellationToken>());
    }

    private static DefaultHttpContext Context(IServiceProvider services, string query)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "GET";
        context.Request.RouteValues["eventId"] = EventId.ToString();
        context.Request.QueryString = new(query);
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Theory]
    [InlineData("admin", "xlsx")]
    [InlineData("admin", "pdf")]
    [InlineData("organizer", "xlsx")]
    [InlineData("organizer", "pdf")]
    [InlineData("event", "xlsx")]
    [InlineData("event", "pdf")]
    public async Task ExportRoutes_ReturnFilesWithAppliedRangeAndDistance(string scope, string format)
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetAdminDashboardQuery>(), Arg.Any<CancellationToken>())
            .Returns(new AdminDashboardDto(0, 0, 0, 0, 0, [], [], Day, Day, DashboardDateRange.TimeZone, DateTime.UtcNow));
        mediator.Send(Arg.Any<ListPagedAuditEntriesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PagedAuditEntriesResult([], 0, 1, 6));
        mediator.Send(Arg.Any<GetOrganizerDashboardQuery>(), Arg.Any<CancellationToken>())
            .Returns(new OrganizerDashboardDto(0, [], 0, 0, [], [], DateTime.UtcNow));
        mediator.Send(Arg.Any<GetOrganizerWalletQuery>(), Arg.Any<CancellationToken>())
            .Returns(new OrganizerWalletDto(0, 0, 0, 0, "VND", null, []));
        mediator.Send(Arg.Any<GetEventDashboardQuery>(), Arg.Any<CancellationToken>()).Returns(EventData());
        mediator.Send(Arg.Any<GetRegistrationTrendQuery>(), Arg.Any<CancellationToken>()).Returns(Trend());
        mediator.Send(Arg.Any<GetRevenueTrendQuery>(), Arg.Any<CancellationToken>()).Returns(Revenue());
        mediator.Send(Arg.Any<GetEventFinanceSummaryQuery>(), Arg.Any<CancellationToken>()).Returns(Finance());
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(mediator);
        await using var app = builder.Build();
        app.MapDashboardExportEndpoints();
        var route = scope switch {
            "admin" => "/api/v1/iam/admin/dashboard/export",
            "organizer" => "/api/v1/event/organizer/dashboard/export",
            _ => "/api/v1/event/organizer/{eventId:guid}/dashboard/export" };
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(x => x.Endpoints).OfType<RouteEndpoint>()
            .Single(x => x.RoutePattern.RawText == route);
        var context = Context(app.Services, $"?format={format}&from=2026-11-15&to=2026-11-15&distanceId={DistanceId}");
        await endpoint.RequestDelegate!(context);
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Contains("attachment", context.Response.Headers.ContentDisposition.ToString());
        Assert.Contains($"20261115-20261115.{format}", context.Response.Headers.ContentDisposition.ToString());
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        var bytes = ((MemoryStream)context.Response.Body).ToArray();
        if (format == "xlsx")
        {
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", context.Response.ContentType);
            using var workbook = new XLWorkbook(new MemoryStream(bytes));
            if (scope == "event") Assert.Equal(3, workbook.Worksheet("Đăng ký mới").Cell(9, 2).GetValue<int>());
        }
        else Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        if (scope == "event")
            await mediator.Received(1).Send(Arg.Is<GetRegistrationTrendQuery>(q => q.EventId == EventId && q.From == Day && q.To == Day), Arg.Any<CancellationToken>());
        if (scope == "admin")
            await mediator.Received(1).Send(Arg.Is<GetAdminDashboardQuery>(q => q.From == Day && q.To == Day), Arg.Any<CancellationToken>());
    }
}
