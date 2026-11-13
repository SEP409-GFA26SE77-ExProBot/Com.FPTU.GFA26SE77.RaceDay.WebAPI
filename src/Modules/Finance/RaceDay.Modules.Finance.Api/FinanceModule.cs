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
        services.AddValidatorsFromAssembly(typeof(CreatePayoutCommandValidator).Assembly);
        services.AddFinanceInfrastructure(configuration);
        return services;
    }

    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1/finance")
            .WithTags(ApiTags.Finance)
            .RequireRateLimiting("public");

        var organizer = api.MapGroup("/organizer").RequireAuthorization(AuthPolicies.PublicUser);

        organizer.MapGet("/wallet", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetOrganizerWalletQuery(), ct)))
            .WithSummary("Kho ví organizer — ledger từng giải + phase giải ngân (too_early/advance/final)");

        organizer.MapGet("/events/ledgers", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new ListEventLedgersQuery(), ct)));

        organizer.MapPut("/bank-account", async (BankAccountDto body, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new SaveOrganizerBankAccountCommand(body), ct)))
            .WithSummary("Lưu TKNH giải ngân (user-level; fallback event beneficiary)");

        organizer.MapGet("/payouts", async (Guid? eventId, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new ListPayoutRequestsQuery(eventId), ct)));

        organizer.MapPost("/payouts", async (CreatePayoutRequestDto body, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new CreatePayoutCommand(body), ct)))
            .WithSummary("Xin giải ngân từ túi 1 giải (đợt 1 T-4 tuần 30%; đợt 2 sau giải). Manager duyệt.");

        organizer.MapGet("/events/{eventId:guid}/summary", async (Guid eventId, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetEventFinanceSummaryQuery(eventId), ct)));

        var runner = api.MapGroup("/runner").RequireAuthorization(AuthPolicies.PublicUser);

        runner.MapGet("/refunds", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new ListMyRefundsQuery(), ct)));

        runner.MapGet("/refunds/quote", async (
            Guid paymentId,
            bool? systemErrorClaim,
            IMediator m,
            CancellationToken ct) =>
            Results.Ok(await m.Send(
                new GetRefundQuoteQuery(paymentId, systemErrorClaim == true),
                ct)))
            .WithSummary("Quote VĐV: lý do cá nhân = 0%; systemErrorClaim=true → đề xuất 100% chờ duyệt");

        runner.MapPost("/refunds", async (CreateRefundRequestDto body, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new CreateRefundCommand(body), ct)))
            .WithSummary("Chỉ SystemErrorClaim — tạo khiếu nại chờ Manager/Admin duyệt (100%); lý do khác từ chối");

        // Manager (hoặc Admin) duyệt giải ngân — không để PublicUser.
        var manager = app.MapGroup("/api/v1/finance/manager")
            .WithTags(ApiTags.Finance)
            .RequireAuthorization(AuthPolicies.ManagerOrAdmin)
            .RequireRateLimiting("public");

        manager.MapGet("/payouts", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new ListAdminPayoutsQuery(), ct)))
            .WithSummary("Danh sách yêu cầu giải ngân (pending / approved / transferred / rejected)");

        manager.MapPost("/payouts/{id:guid}/approve", async (Guid id, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new ApproveAdminPayoutCommand(id), ct)))
            .WithSummary("Bước 1: duyệt lệnh rút (chưa chuyển khoản)");

        manager.MapPost("/payouts/{id:guid}/confirm-transfer", async (Guid id, ConfirmTransferBody body, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new ConfirmTransferAdminPayoutCommand(id, body.ReceiptUrl), ct)))
            .WithSummary("Bước 2: xác nhận đã CK ngoài hệ thống + URL biên nhận bắt buộc → transferred");

        manager.MapPost("/payouts/{id:guid}/reject", async (Guid id, RejectBody body, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new RejectAdminPayoutCommand(id, body.Reason), ct)))
            .WithSummary("Từ chối (pending/approved chưa CK) → nhả reserve trên ledger");

        manager.MapGet("/system-error-refunds", async (bool? pendingOnly, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new ListSystemErrorRefundsQuery(pendingOnly != false), ct)))
            .WithSummary("Danh sách khiếu nại hoàn 100% do lỗi hệ thống (chờ duyệt mặc định)");

        manager.MapPost("/system-error-refunds/{id:guid}/approve", async (Guid id, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new ApproveSystemErrorRefundCommand(id), ct)))
            .WithSummary("Xác minh lỗi hệ thống → hoàn 100% + hủy slot/BIB");

        manager.MapPost("/system-error-refunds/{id:guid}/reject", async (Guid id, RejectBody body, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new RejectSystemErrorRefundCommand(id, body.Reason), ct)))
            .WithSummary("Từ chối khiếu nại lỗi hệ thống");

        // Finance ops UI/API admin đã gỡ — payout + system-error-refunds chỉ /finance/manager.
        // Platform fee / refund policy = seed mặc định trong DB.

        return app;
    }

    private sealed record ConfirmTransferBody(string ReceiptUrl);
    private sealed record RejectBody(string Reason);
}
