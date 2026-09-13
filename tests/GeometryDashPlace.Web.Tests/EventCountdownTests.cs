using GeometryDashPlace.Web.Events;

namespace GeometryDashPlace.Web.Tests;

public sealed class EventCountdownTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, 2, 5, 9, 0, "2D 05:09:00")]
    [InlineData(0, 0, 0, 2, 5, "00:02:05")]
    [InlineData(0, 0, 0, 0, 0, "STARTING...")]
    public void Detailed_FormatsRemainingTime(
        int weeks,
        int days,
        int hours,
        int minutes,
        int seconds,
        string expected)
    {
        var startsAt = Now
            .AddDays(weeks * 7 + days)
            .AddHours(hours)
            .AddMinutes(minutes)
            .AddSeconds(seconds);

        Assert.Equal(expected, EventCountdown.Detailed(startsAt, Now));
    }

    [Theory]
    [InlineData(2, 0, 0, "IN 2D")]
    [InlineData(0, 5, 0, "IN 5H")]
    [InlineData(0, 0, 25, "IN 25M")]
    [InlineData(0, 0, 0, "SOON")]
    public void Notice_UsesCompactLargestUnit(
        int days,
        int hours,
        int minutes,
        string expected)
    {
        var startsAt = Now.AddDays(days).AddHours(hours).AddMinutes(minutes);

        Assert.Equal(expected, EventCountdown.Notice(startsAt, Now));
    }
}
