namespace RaceDay.Modules.Photo.Application.Abstractions;

/// <summary>
/// OCR thresholds + worker concurrency (bound to CPU/RAM; align with OCR MAX_INFLIGHT).
/// </summary>
public interface IPhotoOcrSettings
{
    double ThresholdAuto { get; }
    double ThresholdReviewMin { get; }

    /// <summary>Max photos processed in parallel by WebAPI worker (1–2 recommended on CPU).</summary>
    int WorkerMaxParallel { get; }

    int WorkerIdleDelaySeconds { get; }
    int WorkerBusyDelaySeconds { get; }
}
