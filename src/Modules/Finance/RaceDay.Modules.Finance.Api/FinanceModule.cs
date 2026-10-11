using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Modules.Finance.Application.Admin;
using RaceDay.Modules.Finance.Application.Payouts;
using RaceDay.Modules.Finance.Application.Refunds;
using RaceDay.Modules.Finance.Application.Wallet;
using RaceDay.Modules.Finance.Infrastructure;
using RaceDay.Shared.Web.Auth;
using RaceDay.Shared.Web.OpenApi;

namespace RaceDay.Modules.Finance.Api;

public static class FinanceModule
{
    public static IServiceCollection AddFinanceModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(GetOrganizerWalletQuery).Assembly));
        services.AddValidatorsFromAssembly(typeof(CreatePayoutCommandValidator).Assembly);
        services.AddFinanceInfrastructure(configuration);
        return services;
    }

    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1/finance")
            .WithTags(ApiTags.Finance)
            .RequireRateLimiting("public");

        api.MapGet("/status", () => Results.Ok(new
        {
            module = "finance",
            status = "ready",
            policy = "platform fee + withdraw caps (Admin); refund 100% while RegistrationOpen, 50% after close, 0% at/after gun time"
        }));

        var organizer = api.MapGroup("/organizer").RequireAuthorization(AuthPolicies.PublicUser);

        organizer.MapGet("/wallet", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetOrganizerWalletQuery(), ct)))
            .WithSummary("Ví organizer — rollup ledger theo từng giải");

        organizer.MapGet("/events/ledgers", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new ListEventLedgersQuery(), ct)));

        organizer.MapPut("/bank-account", async (BankAccountDto body, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new SaveOrganizerBankAccountCommand(body), ct)))
            .WithSummary("Lưu TKNH giải ngân (user-level; fallback event beneficiary)");

        organizer.MapGet("/payouts", async (Guid? eventId, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new ListPayoutRequestsQuery(eventId), ct)));

        organizer.MapPost("/payouts", async (CreatePayoutRequestDto body, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new CreatePayoutCommand(body), ct)))
            .WithSummary("Xin giải ngân từ túi 1 giải (không rút gộp)");

        organizer.MapGet("/events/{eventId:guid}/summary", async (Guid eventId, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetEventFinanceSummaryQuery(eventId), ct)));

        var runner = api.MapGroup("/runner").RequireAuthorization(AuthPolicies.PublicUser);

        runner.MapGet("/refunds", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new ListMyRefundsQuery(), ct)));

        runner.MapPost("/refunds", async (CreateRefundRequestDto body, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new CreateRefundCommand(body), ct)))
            .WithSummary("Hoàn tiền theo % policy × số đã trả trên payment (không lấy giá cự ly hiện tại)");

        var admin = app.MapGroup("/api/v1/finance/admin")
            .WithTags(ApiTags.Finance)
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .RequireRateLimiting("public");

        admin.MapGet("/payouts", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new ListAdminPayoutsQuery(), ct)));

        admin.MapPost("/payouts/{id:guid}/approve", async (Guid id, ApproveBody? body, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new ApproveAdminPayoutCommand(id, body?.ReceiptUrl), ct)))
            .WithSummary("Duyệt / chuyển khoản giải ngân → transferred");

        admin.MapPost("/payouts/{id:guid}/reject", async (Guid id, RejectBody body, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new RejectAdminPayoutCommand(id, body.Reason), ct)));

        admin.MapGet("/overview", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetPlatformOverviewQuery(), ct)));

        admin.MapGet("/settings", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetPlatformSettingsQuery(), ct)));

        admin.MapPut("/settings", async (PlatformFeeSettingsDto body, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new SavePlatformSettingsCommand(body), ct)))
            .WithSummary("Phí nền tảng + % rút + trần hoàn tiền (platform)");

        return app;
    }

    private sealed record ApproveBody(string? ReceiptUrl);
    private sealed record RejectBody(string Reason);
}
