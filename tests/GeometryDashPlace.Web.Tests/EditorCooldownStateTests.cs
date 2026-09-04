using GeometryDashPlace.Web.Components.Editor.State;

namespace GeometryDashPlace.Web.Tests;

public sealed class EditorCooldownStateTests
{
    [Fact]
    public void Synchronize_UsesServerClockForCountdown()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 4, 10, 0, 0, TimeSpan.Zero));
        var cooldown = new EditorCooldownState(clock);
        var serverTime = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

        cooldown.Synchronize(serverTime, serverTime.AddSeconds(65));

        Assert.Equal(TimeSpan.FromSeconds(65), cooldown.Remaining);
        Assert.Equal("01:05", cooldown.DisplayText);
        Assert.False(cooldown.IsReady);
        Assert.False(cooldown.IsUrgent);

        clock.Advance(TimeSpan.FromSeconds(56));

        Assert.Equal("00:09", cooldown.DisplayText);
        Assert.True(cooldown.IsUrgent);

        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal("READY", cooldown.DisplayText);
        Assert.True(cooldown.IsReady);
        Assert.False(cooldown.IsUrgent);
    }

    [Fact]
    public void MissingNextAction_IsImmediatelyReady()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var cooldown = new EditorCooldownState(clock);

        cooldown.Synchronize(clock.GetUtcNow(), null);

        Assert.Null(cooldown.NextActionAt);
        Assert.Equal(TimeSpan.Zero, cooldown.Remaining);
        Assert.Equal("READY", cooldown.DisplayText);
        Assert.True(cooldown.IsReady);
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
