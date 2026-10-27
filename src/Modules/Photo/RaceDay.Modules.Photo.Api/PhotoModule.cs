using System.Text.Json.Serialization;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Photo.Application.Disputes;
using RaceDay.Modules.Photo.Application.Photos;
using RaceDay.Modules.Photo.Infrastructure;
using RaceDay.Shared.Web.Auth;
using RaceDay.Shared.Web.OpenApi;

namespace RaceDay.Modules.Photo.Api;

public static class PhotoModule
{
    public static IServiceCollection AddPhotoModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatorsFromAssembly(typeof(BulkUploadPhotosCommand).Assembly);
        services.AddPhotoInfrastructure(configuration);
        return services;
    }

    public static IEndpointRouteBuilder MapPhotoEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1/photo")
            .WithTags(ApiTags.Photo)
            .RequireRateLimiting("public");

        api.MapGet("/status", () => Results.Ok(new { module = "photo", status = "ready" }))
            .WithSummary("Trạng thái module Photo");

        MapPhotographerEndpoints(api);
        MapRunnerEndpoints(api);
        MapInternalEndpoints(api);
        return app;
    }

    private static void MapPhotographerEndpoints(RouteGroupBuilder api)
    {
        var pho = api.MapGroup("/photographer")
            .RequireAuthorization(AuthPolicies.PublicUser)
            .RequireRateLimiting("authenticated");

        // FE chia lô ~40MB; chỉ nới body trên endpoint này (không đổi Kestrel global).
        const long bulkUploadBodyLimitBytes = 64L * 1024 * 1024;

        pho.MapPost("/events/{eventId:guid}/photos/bulk-upload", async (
            Guid eventId,
            HttpRequest request,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var maxBody = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (maxBody is { IsReadOnly: false })
            {
                maxBody.MaxRequestBodySize = bulkUploadBodyLimitBytes;
            }

            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { message = "Expect multipart/form-data." });
            }

            var form = await request.ReadFormAsync(ct);
            var files = form.Files
                .Where(f => f.Length > 0)
                .Select(f => new BulkUploadPhotoFile(
                    f.OpenReadStream(),
                    f.FileName,
                    string.IsNullOrWhiteSpace(f.ContentType) ? "application/octet-stream" : f.ContentType,
                    f.Length))
                .ToList();

            var zone = form["zone"].FirstOrDefault();
            var result = await mediator.Send(new BulkUploadPhotosCommand(eventId, files, zone), ct);
            return Results.Accepted($"/api/v1/photo/photographer/events/{eventId}/photos/pending", result);
        })
            .DisableAntiforgery()
            .WithSummary("Bulk upload ảnh (Pending) — Active Photographer của event")
            .WithDescription("Multipart ≤25MB/file, ≤500 file/request. JWT Photographer không đủ — cần staff Active role Photographer.");

        pho.MapGet("/events/{eventId:guid}/photos/pending", async (
            Guid eventId, int? page, int? pageSize, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                new ListPhotographerPhotosQuery(eventId, "pending", page ?? 1, pageSize ?? 50), ct)))
            .WithSummary("Danh sách ảnh pending/queued/processing/failed");

        pho.MapGet("/events/{eventId:guid}/photos/queue", async (
            Guid eventId, int? page, int? pageSize, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                new ListPhotographerPhotosQuery(eventId, "queue", page ?? 1, pageSize ?? 50), ct)))
            .WithSummary("Hàng OCR còn chạy (pending/queued/processing — không gồm failed)");

        pho.MapGet("/events/{eventId:guid}/photos/matched", async (
            Guid eventId, int? page, int? pageSize, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                new ListPhotographerPhotosQuery(eventId, "matched", page ?? 1, pageSize ?? 50), ct)))
            .WithSummary("Ảnh đã khớp (matched + manual_corrected) — gồm cả chưa/đã publish");

        pho.MapGet("/events/{eventId:guid}/photos/ready", async (
            Guid eventId, int? page, int? pageSize, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                new ListPhotographerPhotosQuery(eventId, "ready", page ?? 1, pageSize ?? 50), ct)))
            .WithSummary("Ảnh đã khớp, chưa publish — chờ photographer xuất bản");

        pho.MapGet("/events/{eventId:guid}/photos/published", async (
            Guid eventId, int? page, int? pageSize, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                new ListPhotographerPhotosQuery(eventId, "published", page ?? 1, pageSize ?? 50), ct)))
            .WithSummary("Ảnh đã publish — runner search được");

        pho.MapPost("/events/{eventId:guid}/photos/publish", async (
            Guid eventId,
            PublishPhotosBody? body,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                new PublishPhotosCommand(
                    eventId,
                    body?.PhotoIds,
                    body?.AllReady ?? false), ct)))
            .WithSummary("Publish ảnh đã khớp → hiện trên search runner (bulk / allReady)");

        pho.MapGet("/events/{eventId:guid}/photos/review", async (
            Guid eventId, int? page, int? pageSize, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                new ListPhotographerPhotosQuery(eventId, "review", page ?? 1, pageSize ?? 50), ct)))
            .WithSummary("Ảnh cần review / runner reported");

        pho.MapGet("/events/{eventId:guid}/photos/discarded", async (
            Guid eventId, int? page, int? pageSize, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                new ListPhotographerPhotosQuery(eventId, "discarded", page ?? 1, pageSize ?? 50), ct)))
            .WithSummary("Ảnh đã loại (OCR no_bib / photographer reject)");

        pho.MapPost("/events/{eventId:guid}/photos/process", async (
            Guid eventId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ProcessPendingPhotosCommand(eventId), ct)))
            .WithSummary("Enqueue OCR cho ảnh pending (idempotent → Queued)");

        pho.MapPatch("/events/{eventId:guid}/photos/{photoId:guid}", async (
            Guid eventId,
            Guid photoId,
            CorrectBibBody body,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                new CorrectPhotoBibCommand(eventId, photoId, body.Bib), ct)))
            .WithSummary("Sửa BIB (chỉ NeedsReview / RunnerReported) → ManualCorrected");

        pho.MapPost("/events/{eventId:guid}/photos/{photoId:guid}/discard", async (
            Guid eventId,
            Guid photoId,
            DiscardPhotoBody? body,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                new DiscardPhotoCommand(eventId, photoId, body?.Reason), ct)))
            .WithSummary("Reject / loại ảnh (needs_review, pending, failed) → Discarded");

        pho.MapGet("/events/{eventId:guid}/photos/{photoId:guid}", async (
            Guid eventId,
            Guid photoId,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                new GetPhotographerPhotoDetailQuery(eventId, photoId), ct)))
            .WithSummary("Chi tiết 1 ảnh (photographer — mọi status)");

        pho.MapGet("/events/{eventId:guid}/disputes", async (
            Guid eventId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListOpenEventDisputesQuery(eventId), ct)))
            .WithSummary("Khiếu nại Open của giải");

        pho.MapPost("/events/{eventId:guid}/disputes/{id:guid}/approve", async (
            Guid eventId, Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ApproveDisputeCommand(eventId, id), ct)))
            .WithSummary("Approve khiếu nại → ảnh về NeedsReview");

        pho.MapPost("/events/{eventId:guid}/disputes/{id:guid}/reject", async (
            Guid eventId, Guid id, RejectDisputeBody body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                new RejectDisputeCommand(eventId, id, body.Reason), ct)))
            .WithSummary("Reject khiếu nại + lý do");
    }

    private static void MapRunnerEndpoints(RouteGroupBuilder api)
    {
        var runner = api.MapGroup("")
            .RequireAuthorization(AuthPolicies.PublicUser)
            .RequireRateLimiting("authenticated");

        runner.MapGet("/photos", async (
            [FromQuery] Guid eventId,
            [FromQuery] string bib,
            int? page,
            int? pageSize,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                new SearchPhotosQuery(eventId, bib, page ?? 1, pageSize ?? 50), ct)))
            .WithSummary("Search ảnh theo eventId + BIB (DB index, không gọi OCR)");

        runner.MapGet("/photos/{photoId:guid}", async (
            Guid photoId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GetPhotoDetailQuery(photoId), ct)))
            .WithSummary("Chi tiết ảnh");

        runner.MapGet("/photos/{photoId:guid}/download", async (
            Guid photoId, IMediator mediator, CancellationToken ct) =>
        {
            var file = await mediator.Send(new DownloadPhotoQuery(photoId), ct);
            return Results.File(
                file.Content,
                file.ContentType,
                file.FileName,
                enableRangeProcessing: false);
        })
            .WithSummary("Tải ảnh về máy (proxy storage — tránh CORS trình duyệt)");

        runner.MapPost("/photos/{photoId:guid}/disputes", async (
            Guid photoId, CreateDisputeBody body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new CreatePhotoDisputeCommand(photoId, body.Reason), ct)))
            .WithSummary("Runner khiếu nại ảnh → RunnerReported");

        runner.MapGet("/me/disputes", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListMyDisputesQuery(), ct)))
            .WithSummary("Khiếu nại của tôi");

        runner.MapGet("/me/disputes/{id:guid}", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GetMyDisputeQuery(id), ct)))
            .WithSummary("Chi tiết khiếu nại của tôi");
    }

    private static void MapInternalEndpoints(RouteGroupBuilder api)
    {
        var internalApi = api.MapGroup("/internal")
            .AllowAnonymous()
            .RequireRateLimiting("public");

        internalApi.MapPost("/photos/{photoId:guid}/ocr-result", async (
            Guid photoId,
            OcrResultBody body,
            HttpRequest http,
            IConfiguration config,
            IMediator mediator,
            CancellationToken ct) =>
        {
            if (!IsValidServiceKey(http, config))
            {
                return Results.Unauthorized();
            }

            await mediator.Send(new ApplyOcrResultCommand(
                photoId,
                body.Status,
                (body.Tags ?? [])
                    .Select(t => new OcrResultTagDto(t.Bib, t.Confidence, t.Bbox))
                    .ToList(),
                body.DuplicateOf,
                body.Reasons), ct);
            return Results.NoContent();
        })
            .WithSummary("OCR callback (X-Service-Key) — map status/tags, discard no_bib");

        internalApi.MapGet("/events/{eventId:guid}/bibs", async (
            Guid eventId,
            HttpRequest http,
            IConfiguration config,
            IEventBibRosterQuery rosterQuery,
            CancellationToken ct) =>
        {
            if (!IsValidServiceKey(http, config))
            {
                return Results.Unauthorized();
            }

            var snapshot = await rosterQuery.GetConfirmedBibsAsync(eventId, ct);
            return Results.Ok(new
            {
                event_id = snapshot.EventId,
                version = snapshot.Version,
                bibs = snapshot.Bibs
            });
        })
            .WithSummary("Roster BIB snapshot cho OCR cache (X-Service-Key)");
    }

    private static bool IsValidServiceKey(HttpRequest http, IConfiguration config)
    {
        var expected = config["Photo:Ocr:ServiceKey"]
            ?? config["Photo:Internal:ServiceKey"];
        if (string.IsNullOrWhiteSpace(expected))
        {
            return false;
        }

        if (!http.Headers.TryGetValue("X-Service-Key", out var provided))
        {
            return false;
        }

        return string.Equals(provided.ToString(), expected, StringComparison.Ordinal);
    }

    private sealed record CorrectBibBody(string Bib);
    private sealed record DiscardPhotoBody(string? Reason);
    private sealed record RejectDisputeBody(string Reason);
    private sealed record CreateDisputeBody(string Reason);
    private sealed record PublishPhotosBody(
        IReadOnlyList<Guid>? PhotoIds = null,
        bool AllReady = false);

    private sealed record OcrResultBody(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("tags")] IReadOnlyList<OcrResultTagBody>? Tags,
        [property: JsonPropertyName("duplicate_of")] string? DuplicateOf = null,
        [property: JsonPropertyName("reasons")] IReadOnlyList<string>? Reasons = null);

    private sealed record OcrResultTagBody(
        [property: JsonPropertyName("bib")] string Bib,
        [property: JsonPropertyName("confidence")] double Confidence,
        [property: JsonPropertyName("bbox")] IReadOnlyList<double>? Bbox = null);
}

