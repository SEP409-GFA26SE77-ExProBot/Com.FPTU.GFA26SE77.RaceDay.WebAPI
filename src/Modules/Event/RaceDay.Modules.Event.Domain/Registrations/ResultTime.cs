using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Domain.Registrations;

/// <summary>Same AwayFromZero rounding the live finish command uses for ResultSeconds.</summary>
public static class ResultTime
{
    public static long RoundBetween(DateTime startUtc, DateTime finishUtc)
        => RoundElapsed((finishUtc - startUtc).TotalSeconds);

    public static long RoundElapsed(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
        {
            throw new DomainException("Kết quả finish không hợp lệ.");
        }

        return (long)Math.Round(seconds, MidpointRounding.AwayFromZero);
    }
}
