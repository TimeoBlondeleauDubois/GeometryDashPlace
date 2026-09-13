using System.Globalization;

namespace GeometryDashPlace.Web.Events;

public static class EventCountdown
{
    public static string Detailed(DateTimeOffset startsAt, DateTimeOffset now)
    {
        var remaining = startsAt - now;
        if (remaining <= TimeSpan.Zero)
        {
            return "STARTING...";
        }

        return remaining.TotalDays >= 1
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{(int)remaining.TotalDays}D {remaining.Hours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}");
    }

    public static string Notice(DateTimeOffset startsAt, DateTimeOffset now)
    {
        var remaining = startsAt - now;
        if (remaining <= TimeSpan.Zero || remaining.TotalMinutes < 1)
        {
            return "SOON";
        }
        if (remaining.TotalDays >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"IN {(int)remaining.TotalDays}D");
        }
        if (remaining.TotalHours >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"IN {(int)remaining.TotalHours}H");
        }
        return string.Create(CultureInfo.InvariantCulture, $"IN {(int)remaining.TotalMinutes}M");
    }
}
