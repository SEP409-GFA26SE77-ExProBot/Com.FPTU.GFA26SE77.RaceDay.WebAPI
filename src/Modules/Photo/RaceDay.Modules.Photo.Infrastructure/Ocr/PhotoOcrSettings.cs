using Microsoft.Extensions.Configuration;
using RaceDay.Modules.Photo.Application.Abstractions;

namespace RaceDay.Modules.Photo.Infrastructure.Ocr;

public sealed class PhotoOcrSettings(IConfiguration configuration) : IPhotoOcrSettings
{
    public double ThresholdAuto =>
        ParseDouble(configuration["Photo:Ocr:ThresholdAuto"], 0.85);

    public double ThresholdReviewMin =>
        ParseDouble(configuration["Photo:Ocr:ThresholdReviewMin"], 0.50);

    public int WorkerMaxParallel =>
        Math.Clamp(ParseInt(configuration["Photo:Ocr:WorkerMaxParallel"], 3), 1, 8);

    public int WorkerIdleDelaySeconds =>
        Math.Clamp(ParseInt(configuration["Photo:Ocr:WorkerIdleDelaySeconds"], 5), 1, 60);

    public int WorkerBusyDelaySeconds =>
        Math.Clamp(ParseInt(configuration["Photo:Ocr:WorkerBusyDelaySeconds"], 1), 0, 30);

    private static double ParseDouble(string? value, double fallback)
        => double.TryParse(
            value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var d)
            ? d
            : fallback;

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var n)
            ? n
            : fallback;
}
