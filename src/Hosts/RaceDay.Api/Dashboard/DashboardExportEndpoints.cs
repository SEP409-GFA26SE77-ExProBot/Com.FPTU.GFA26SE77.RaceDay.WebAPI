using MediatR;
using RaceDay.Modules.Event.Application.Dashboard;
using RaceDay.Modules.Finance.Application.Dashboard;
using RaceDay.Modules.Finance.Application.Wallet;
using RaceDay.Modules.Iam.Application.Dashboard;
using RaceDay.Modules.AuditLog.Application.AuditEntries;
using RaceDay.Shared.Application.Dashboard;
using RaceDay.Shared.Domain.Exceptions;
using RaceDay.Shared.Web.Auth;
using RaceDay.Shared.Web.OpenApi;

namespace RaceDay.Api.Dashboard;

// Host composes existing module queries; exports use the same authorization and calculations as the dashboards.
public static class DashboardExportEndpoints
{
    public static IEndpointRouteBuilder MapDashboardExportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/iam/admin/dashboard/export", async (string? format, DateOnly? from, DateOnly? to,
            IMediator mediator, HttpContext context, CancellationToken ct) =>
        {
            ValidateFormat(format);
            var data = await mediator.Send(new GetAdminDashboardQuery(from, to), ct);
            var audit = await mediator.Send(new ListPagedAuditEntriesQuery(1, 6), ct);
            return Download(DashboardReports.Admin(data, audit.Items), format, "admin-dashboard", context);
        }).RequireAuthorization(AuthPolicies.AdminOnly).RequireRateLimiting("public")
            .WithTags(ApiTags.IamAdmin).WithSummary("Xuất dashboard Admin dạng xlsx hoặc pdf");

        var organizer = app.MapGroup("/api/v1/event/organizer")
            .RequireAuthorization(AuthPolicies.PublicUser, AuthPolicies.HumanUser).RequireRateLimiting("public")
            .WithTags(ApiTags.EventOrganizer);
        organizer.MapGet("/dashboard/export", async (string? format, DateOnly? from, DateOnly? to,
            IMediator mediator, HttpContext context, CancellationToken ct) =>
        {
            ValidateFormat(format);
            var range = DashboardDateRange.Create(from, to, DateTime.UtcNow);
            // Sequential: handlers may share scoped DbContexts.
            var data = await mediator.Send(new GetOrganizerDashboardQuery(), ct);
            var revenue = await mediator.Send(new GetRevenueTrendQuery(null, range.From, range.To), ct);
            var wallet = await mediator.Send(new GetOrganizerWalletQuery(), ct);
            return Download(DashboardReports.Organizer(data, wallet, revenue), format, "organizer-dashboard", context);
        }).WithSummary("Xuất dashboard các giải của Organizer dạng xlsx hoặc pdf");

        organizer.MapGet("/{eventId:guid}/dashboard/export", async (Guid eventId, Guid? distanceId,
            string? format, DateOnly? from, DateOnly? to, IMediator mediator, HttpContext context, CancellationToken ct) =>
        {
            ValidateFormat(format);
            var range = DashboardDateRange.Create(from, to, DateTime.UtcNow);
            // Check strict event ownership before loading any other module's data.
            var data = await mediator.Send(new GetEventDashboardQuery(eventId), ct);
            if (distanceId.HasValue && !data.Distances.Any(d => d.DistanceId == distanceId))
                throw new DomainException("Cự ly không thuộc giải đang xem.");
            var trend = await mediator.Send(new GetRegistrationTrendQuery(eventId, range.From, range.To), ct);
            var revenue = await mediator.Send(new GetRevenueTrendQuery(eventId, range.From, range.To), ct);
            var finance = await mediator.Send(new GetEventFinanceSummaryQuery(eventId), ct);
            return Download(DashboardReports.Event(data, trend, revenue, finance, distanceId), format,
                $"event-dashboard-{eventId}", context);
        }).WithSummary("Xuất dashboard một giải; distanceId chỉ lọc biểu đồ đăng ký");
        return app;
    }

    private static void ValidateFormat(string? format)
    {
        if (format is not ("xlsx" or "pdf"))
            throw new DomainException("Định dạng xuất phải là xlsx hoặc pdf.");
    }

    private static IResult Download(DashboardReport report, string? format, string name, HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var bytes = format == "pdf" ? DashboardReportWriter.Pdf(report) : DashboardReportWriter.Excel(report);
        var mime = format == "pdf" ? "application/pdf" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        return Results.File(bytes, mime, $"{name}-{report.From:yyyyMMdd}-{report.To:yyyyMMdd}.{format}");
    }
}
