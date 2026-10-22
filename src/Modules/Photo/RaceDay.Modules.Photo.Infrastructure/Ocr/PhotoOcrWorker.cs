using Microsoft.Extensions.DependencyInjection;
using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Photo.Application.Abstractions;
using RaceDay.Modules.Photo.Application.Photos;

namespace RaceDay.Modules.Photo.Infrastructure.Ocr;

/// <summary>
/// Polls Queued photos → OCR service. Standalone OCR returns result inline;
/// worker applies via <see cref="ApplyOcrResultCommand"/> in a fresh scope.
/// Concurrency capped (WorkerMaxParallel) to stay within CPU/RAM + OCR MAX_INFLIGHT.
/// </summary>
public sealed class PhotoOcrWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<PhotoOcrWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var hadWork = false;
            try
            {
                hadWork = await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Photo OCR worker batch failed.");
            }

            int idleSec;
            int busySec;
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var settings = scope.ServiceProvider.GetRequiredService<IPhotoOcrSettings>();
                idleSec = settings.WorkerIdleDelaySeconds;
                busySec = settings.WorkerBusyDelaySeconds;
            }

            var delay = hadWork ? busySec : idleSec;
            if (delay > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(delay), stoppingToken);
            }
        }
    }

    private async Task<bool> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var probe = scopeFactory.CreateAsyncScope();
        var ocrProbe = probe.ServiceProvider.GetRequiredService<IOcrClient>();
        if (!ocrProbe.IsConfigured)
        {
            return false;
        }

        var settings = probe.ServiceProvider.GetRequiredService<IPhotoOcrSettings>();
        var maxParallel = settings.WorkerMaxParallel;
        var photosProbe = probe.ServiceProvider.GetRequiredService<IPhotoAssetRepository>();
        var uowProbe = probe.ServiceProvider.GetRequiredService<IPhotoUnitOfWork>();

        // HttpClient OCR timeout 3p; API restart giữa chừng → Processing kẹt. Reclaim sau 4 phút.
        var stuck = await photosProbe.ListStuckProcessingAsync(
            TimeSpan.FromMinutes(4),
            maxParallel * 4,
            cancellationToken);
        if (stuck.Count > 0)
        {
            foreach (var photo in stuck)
            {
                photo.RequeueStuckProcessing();
                photosProbe.Update(photo);
                logger.LogWarning(
                    "Requeued stuck Processing photo {PhotoId} (UpdatedAtUtc={UpdatedAtUtc:u})",
                    photo.Id,
                    photo.UpdatedAtUtc);
            }

            await uowProbe.SaveChangesAsync(cancellationToken);
        }

        var queued = await photosProbe.ListQueuedAsync(maxParallel, cancellationToken);
        if (queued.Count == 0)
        {
            return stuck.Count > 0;
        }

        var ids = queued.Select(p => p.Id).ToList();
        if (ids.Count == 1)
        {
            await ProcessOneAsync(ids[0], cancellationToken);
            return true;
        }

        await Task.WhenAll(ids.Select(id => ProcessOneAsync(id, cancellationToken)));
        return true;
    }

    private async Task ProcessOneAsync(Guid photoId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var photos = scope.ServiceProvider.GetRequiredService<IPhotoAssetRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IPhotoUnitOfWork>();
        var ocr = scope.ServiceProvider.GetRequiredService<IOcrClient>();
        var rosterQuery = scope.ServiceProvider.GetRequiredService<IEventBibRosterQuery>();

        var photo = await photos.GetByIdWithTagsAsync(photoId, cancellationToken);
        if (photo is null || photo.Status != Domain.PhotoStatus.Queued)
        {
            return;
        }

        try
        {
            var roster = await rosterQuery.GetConfirmedBibsAsync(photo.EventId, cancellationToken);
            var jobId = Guid.NewGuid();
            photo.MarkProcessing(jobId.ToString("N"));
            photos.Update(photo);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            var enqueued = await ocr.EnqueueJobAsync(
                jobId,
                photo.EventId,
                photo.Id,
                photo.Url,
                roster.Version,
                roster.Bibs,
                cancellationToken);

            if (enqueued.HasInlineResult)
            {
                // Fresh scope avoids EF tracking conflict after long OCR call
                await using var applyScope = scopeFactory.CreateAsyncScope();
                var applyMediator = applyScope.ServiceProvider.GetRequiredService<IMediator>();
                await applyMediator.Send(new ApplyOcrResultCommand(
                    photo.Id,
                    enqueued.Status ?? "needs_review",
                    (enqueued.Tags ?? [])
                        .Select(t => new OcrResultTagDto(t.Bib, t.Confidence, t.Bbox))
                        .ToList(),
                    enqueued.DuplicateOf,
                    enqueued.Reasons), cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(enqueued.RemoteJobId))
            {
                await using var updScope = scopeFactory.CreateAsyncScope();
                var updPhotos = updScope.ServiceProvider.GetRequiredService<IPhotoAssetRepository>();
                var updUow = updScope.ServiceProvider.GetRequiredService<IPhotoUnitOfWork>();
                var fresh = await updPhotos.GetByIdWithTagsAsync(photo.Id, cancellationToken);
                if (fresh is not null)
                {
                    fresh.MarkProcessing(enqueued.RemoteJobId);
                    updPhotos.Update(fresh);
                    await updUow.SaveChangesAsync(cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enqueue OCR for photo {PhotoId}", photoId);
            await using var failScope = scopeFactory.CreateAsyncScope();
            var failPhotos = failScope.ServiceProvider.GetRequiredService<IPhotoAssetRepository>();
            var failUow = failScope.ServiceProvider.GetRequiredService<IPhotoUnitOfWork>();
            var fresh = await failPhotos.GetByIdWithTagsAsync(photoId, cancellationToken);
            if (fresh is null)
            {
                return;
            }

            var msg = ex.Message.Length > 480 ? ex.Message[..480] : ex.Message;
            fresh.MarkFailed(msg);
            failPhotos.Update(fresh);
            await failUow.SaveChangesAsync(cancellationToken);
        }
    }
}
