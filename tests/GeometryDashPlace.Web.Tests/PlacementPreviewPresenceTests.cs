using GeometryDashPlace.Web.Realtime;

namespace GeometryDashPlace.Web.Tests;

public sealed class PlacementPreviewPresenceTests
{
    [Fact]
    public void ActivePreview_ExpiresAfterLifetime()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
        var presence = new PlacementPreviewPresence(clock);
        var eventId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        presence.Observe(new PlacementPreview(
            eventId, userId, IsActive: true, Type: "spike"));
        clock.Advance(PlacementPreviewPresence.Lifetime - TimeSpan.FromMilliseconds(1));

        Assert.Empty(presence.TakeExpired());

        clock.Advance(TimeSpan.FromMilliseconds(1));

        var expired = Assert.Single(presence.TakeExpired());
        Assert.Equal(eventId, expired.EventId);
        Assert.Equal(userId, expired.ActorUserId);
        Assert.False(expired.IsActive);
    }

    [Fact]
    public void Heartbeat_RenewsPreviewLifetime()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var presence = new PlacementPreviewPresence(clock);
        var preview = new PlacementPreview(
            Guid.NewGuid(), Guid.NewGuid(), IsActive: true, Type: "block");

        presence.Observe(preview);
        clock.Advance(TimeSpan.FromSeconds(2));
        presence.Observe(preview);
        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Empty(presence.TakeExpired());

        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Single(presence.TakeExpired());
    }

    [Fact]
    public void InactivePreview_IsRemovedWithoutLaterExpiration()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var presence = new PlacementPreviewPresence(clock);
        var eventId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        presence.Observe(new PlacementPreview(
            eventId, userId, IsActive: true, Type: "spike"));
        presence.Observe(new PlacementPreview(eventId, userId, IsActive: false));
        clock.Advance(PlacementPreviewPresence.Lifetime);

        Assert.Empty(presence.TakeExpired());
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
