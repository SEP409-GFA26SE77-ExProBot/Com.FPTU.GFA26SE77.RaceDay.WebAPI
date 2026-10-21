using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Photo.Application.Abstractions;

namespace RaceDay.Modules.Photo.Infrastructure.Ocr;

/// <summary>Polls Queued photos and submits OCR jobs (non-blocking for HTTP upload).</summary>
public sealed class PhotoOcrWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<PhotoOcrWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Photo OCR worker batch failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var photos = scope.ServiceProvider.GetRequiredService<IPhotoAssetRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IPhotoUnitOfWork>();
        var ocr = scope.ServiceProvider.GetRequiredService<IOcrClient>();
        var rosterQuery = scope.ServiceProvider.GetRequiredService<IEventBibRosterQuery>();

        if (!ocr.IsConfigured)
        {
            return;
        }

        var queued = await photos.ListQueuedAsync(20, cancellationToken);
        foreach (var photo in queued)
        {
            try
            {
                var roster = await rosterQuery.GetConfirmedBibsAsync(photo.EventId, cancellationToken);
                var jobId = Guid.NewGuid();
                var remoteId = await ocr.EnqueueJobAsync(
                    jobId,
                    photo.EventId,
                    photo.Id,
                    photo.Url,
                    roster.Version,
                    roster.Bibs,
                    cancellationToken);

                photo.MarkProcessing(remoteId);
                photos.Update(photo);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to enqueue OCR for photo {PhotoId}", photo.Id);
                var msg = ex.Message.Length > 480 ? ex.Message[..480] : ex.Message;
                photo.MarkFailed(msg);
                photos.Update(photo);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
