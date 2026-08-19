using System.Globalization;

namespace NuGetCooldown.Model;

/// <summary>
/// Formats a cooldown window or a package age for humans, choosing days or hours automatically:
/// a day or more reads in days ("7 days", "1.9 days"), anything shorter in hours ("12 hours").
/// This keeps sub-day windows (e.g. pnpm's 24h, a 72-hour policy) legible instead of "0.5 days".
/// </summary>
public static class DurationFormat
{
    /// <summary>Adaptive, singular-aware rendering of <paramref name="duration"/>.</summary>
    public static string Humanize(TimeSpan duration)
    {
        var days = duration.TotalDays;
        return Math.Abs(days) >= 1.0
            ? Format(days, "day")
            : Format(duration.TotalHours, "hour");
    }

    private static string Format(double value, string unit)
    {
        var rounded = Math.Round(value, 1);
        var text = rounded.ToString("0.#", CultureInfo.InvariantCulture);
        return text is "1" or "-1" ? $"{text} {unit}" : $"{text} {unit}s";
    }
}
