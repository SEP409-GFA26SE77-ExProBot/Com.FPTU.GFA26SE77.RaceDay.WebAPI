using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RaceDay.Modules.RaceMonitoring.Application.Rfid;
using RaceDay.Modules.RaceMonitoring.Application.TimingReads;
using RaceDay.Shared.Web.Auth;

namespace RaceDay.Modules.RaceMonitoring.Api;

internal static class RfidEndpoints
{
    public static void MapRfidEndpoints(this RouteGroupBuilder api)
    {
        var gateway = api.MapGroup("/rfid")
            .AllowAnonymous();

        gateway.MapGet("/read-requests/next", async (
            string? readerId,
            Guid? requestId,
            HttpRequest http,
            RfidGatewayOptions options,
            IMediator mediator,
            CancellationToken ct) =>
        {
            if (!Authorized(http, options))
            {
                return Results.Unauthorized();
            }

            return await Send(mediator, new ClaimNextRfidReadRequestQuery(readerId, requestId), ct);
        })
            .WithSummary("Gateway kéo lệnh đọc hoặc chế độ checkpoint")
            .WithDescription(
                "Header X-Service-Key. Query: readerId, requestId?. "
                + "command assign: đang có một yêu cầu của đúng reader. "
                + "command hold: đã xác nhận gán, chờ Bắt đầu quét checkpoint. "
                + "command checkpoint: gửi timing-reads. releaseTagIds là EPC của lượt gán cần xóa khỏi cửa sổ lọc.");

        gateway.MapPost("/read-requests/{requestId:guid}/observations", async (
            Guid requestId,
            RfidObservationRequest body,
            HttpRequest http,
            RfidGatewayOptions options,
            IMediator mediator,
            CancellationToken ct) =>
        {
            if (!Authorized(http, options))
            {
                return Results.Unauthorized();
            }

            return await Send(
                mediator,
                new SubmitRfidObservationsCommand(requestId, body.ReaderId, body.Epcs),
                ct);
        })
            .WithSummary("Gateway nộp EPC của lượt đọc để gán")
            .WithDescription(
                "Header X-Service-Key. Body: { readerId, epcs: [{ tagId, observedAtUtc }] }. "
                + "Không EPC thì NoTag, một EPC thì Observed, nhiều EPC thì Ambiguous. "
                + "Không ghi timing-reads và không tạo timeline.");

        var organizer = api.MapGroup("/rfid")
            .RequireAuthorization(AuthPolicies.PublicUser)
            .RequireRateLimiting("authenticated");

        organizer.MapPost("/read-requests", async (
            CreateRfidReadRequestCommand body,
            IMediator mediator,
            CancellationToken ct) =>
            await Send(mediator, body, ct))
            .WithSummary("Tạo yêu cầu đọc thẻ để gán")
            .WithDescription(
                "JWT chủ giải hoặc admin. Body: { eventId, registrationId, readerId }. "
                + "Registration Confirmed, có BIB, thuộc đúng giải. "
                + "Một reader chỉ một yêu cầu chưa kết thúc; yêu cầu thứ hai trả 409.");

        organizer.MapGet("/read-requests/{requestId:guid}", async (
            Guid requestId,
            IMediator mediator,
            CancellationToken ct) =>
            await Send(mediator, new GetRfidReadRequestQuery(requestId), ct))
            .WithSummary("Xem trạng thái yêu cầu đọc");

        organizer.MapPost("/read-requests/{requestId:guid}/cancel", async (
            Guid requestId,
            IMediator mediator,
            CancellationToken ct) =>
            await Send(mediator, new CancelRfidReadRequestCommand(requestId), ct))
            .WithSummary("Hủy yêu cầu đọc chưa xác nhận")
            .WithDescription("Chỉ người tạo yêu cầu, và phải là chủ giải. Đã Confirmed thì không hủy.");

        organizer.MapPost("/assignments", async (
            ConfirmRfidAssignmentCommand body,
            IMediator mediator,
            CancellationToken ct) =>
            await Send(mediator, body, ct))
            .WithSummary("Xác nhận gán EPC")
            .WithDescription(
                "Body: { requestId, tagId }. Chỉ khi yêu cầu Observed với đúng một EPC. "
                + "Cùng cặp đăng ký và EPC trả dòng hiện có. Xung đột trả 409. "
                + "Không tạo checkpoint_scans.");

        organizer.MapGet("/assignments", async (
            Guid eventId,
            Guid registrationId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            try
            {
                var found = await mediator.Send(new GetRfidAssignmentQuery(eventId, registrationId), ct);
                return found is null
                    ? Results.NotFound(new { error = "Chưa gán EPC cho đăng ký này." })
                    : Results.Ok(found);
            }
            catch (RfidConflictException ex)
            {
                return Conflict(ex);
            }
        })
            .WithSummary("Xem EPC đã gán cho một đăng ký");

        organizer.MapGet("/readers", async (
            Guid eventId,
            IMediator mediator,
            CancellationToken ct) =>
            await Send(mediator, new ListRfidReadersQuery(eventId), ct))
            .WithSummary("Đầu đọc đã cấu hình cho giải")
            .WithDescription("Chỉ readerId, eventId, checkpointId và mode. Không trả service key.");

        organizer.MapPost("/readers/{readerId}/checkpoint-scan", async (
            string readerId,
            IMediator mediator,
            CancellationToken ct) =>
            await Send(mediator, new StartRfidCheckpointScanCommand(readerId), ct))
            .WithSummary("Bắt đầu quét checkpoint")
            .WithDescription(
                "Chủ giải của reader. Gateway đang chờ sau khi xác nhận gán sẽ gửi timing-reads. "
                + "EPC của lượt gán không bị cửa sổ lọc 15 giây chặn ở lượt quét đầu.");
    }

    private static bool Authorized(HttpRequest http, RfidGatewayOptions options)
    {
        var provided = http.Headers.TryGetValue(RfidGatewayAuthenticator.HeaderName, out var header)
            ? header.ToString()
            : null;
        return RfidGatewayAuthenticator.IsValid(provided, options.ServiceKey);
    }

    private static async Task<IResult> Send<T>(IMediator mediator, IRequest<T> request, CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await mediator.Send(request, cancellationToken));
        }
        catch (RfidConflictException ex)
        {
            return Conflict(ex);
        }
    }

    private static IResult Conflict(RfidConflictException ex) =>
        Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status409Conflict);

    private sealed record RfidObservationRequest(string? ReaderId, IReadOnlyList<RfidObservedEpc>? Epcs);
}
