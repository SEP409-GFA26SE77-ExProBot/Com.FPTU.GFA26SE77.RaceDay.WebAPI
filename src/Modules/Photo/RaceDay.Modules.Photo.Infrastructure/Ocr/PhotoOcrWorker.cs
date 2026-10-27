using System.Collections.Concurrent;
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
/// Continuous fill: luôn giữ tối đa <see cref="IPhotoOcrSettings.WorkerMaxParallel"/> job
/// in-flight (claim tiếp ngay khi một slot trống — không chờ hết batch).
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
                hadWork = await ProcessWaveAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Photo OCR worker wave failed.");
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

    private async Task<bool> ProcessWaveAsync(CancellationToken cancellationToken)
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

        var rosterCache = new ConcurrentDictionary<Guid, EventBibRosterSnapshot>();
        var gate = new SemaphoreSlim(maxParallel, maxParallel);
        var inflight = new List<Task>();
        var started = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            await gate.WaitAsync(cancellationToken);

            ClaimedPhoto? claimed;
            try
            {
                claimed = await TryClaimNextAsync(rosterCache, cancellationToken);
            }
            catch (Exception ex)
            {
                gate.Release();
                logger.LogWarning(ex, "OCR claim failed.");
                break;
            }

            if (claimed is null)
            {
                gate.Release();
                break;
            }

            started++;
            var job = claimed;
            inflight.Add(Task.Run(async () =>
            {
                try
                {
                    await RunOcrAsync(job, cancellationToken);
                }
                finally
                {
                    gate.Release();
                }
            }, cancellationToken));
        }

        if (inflight.Count > 0)
        {
            await Task.WhenAll(inflight);
        }

        return started > 0 || stuck.Count > 0;
    }

    private async Task<ClaimedPhoto?> TryClaimNextAsync(
        ConcurrentDictionary<Guid, EventBibRosterSnapshot> rosterCache,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var photos = scope.ServiceProvider.GetRequiredService<IPhotoAssetRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IPhotoUnitOfWork>();
        var rosterQuery = scope.ServiceProvider.GetRequiredService<IEventBibRosterQuery>();

        var next = await photos.ListQueuedAsync(1, cancellationToken);
        if (next.Count == 0)
        {
            return null;
        }

        var photo = next[0];
        EventBibRosterSnapshot roster;
        if (!rosterCache.TryGetValue(photo.EventId, out roster!))
        {
            roster = await rosterQuery.GetConfirmedBibsAsync(photo.EventId, cancellationToken);
            rosterCache.TryAdd(photo.EventId, roster);
        }

        var jobId = Guid.NewGuid();
        photo.MarkProcessing(jobId.ToString("N"));
        photos.Update(photo);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ClaimedPhoto(photo.Id, photo.EventId, photo.Url, jobId, roster);
    }

    private async Task RunOcrAsync(ClaimedPhoto job, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var ocr = scope.ServiceProvider.GetRequiredService<IOcrClient>();

            var enqueued = await ocr.EnqueueJobAsync(
                job.JobId,
                job.EventId,
                job.PhotoId,
                job.ImageUrl,
                job.Roster.Version,
                job.Roster.Bibs,
                cancellationToken);

            if (enqueued.HasInlineResult)
            {
                await using var applyScope = scopeFactory.CreateAsyncScope();
                var applyMediator = applyScope.ServiceProvider.GetRequiredService<IMediator>();
                await applyMediator.Send(new ApplyOcrResultCommand(
                    job.PhotoId,
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
                var fresh = await updPhotos.GetByIdWithTagsAsync(job.PhotoId, cancellationToken);
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
            logger.LogWarning(ex, "Failed to enqueue OCR for photo {PhotoId}", job.PhotoId);
            await using var failScope = scopeFactory.CreateAsyncScope();
            var failPhotos = failScope.ServiceProvider.GetRequiredService<IPhotoAssetRepository>();
            var failUow = failScope.ServiceProvider.GetRequiredService<IPhotoUnitOfWork>();
            var fresh = await failPhotos.GetByIdWithTagsAsync(job.PhotoId, cancellationToken);
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

    private sealed record ClaimedPhoto(
        Guid PhotoId,
        Guid EventId,
        string ImageUrl,
        Guid JobId,
        EventBibRosterSnapshot Roster);
}
