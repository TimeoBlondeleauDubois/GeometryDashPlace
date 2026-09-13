using GeometryDashPlace.Web.Profiles;

namespace GeometryDashPlace.Web.Tests;

public sealed class PlayerBadgeRulesTests
{
    [Fact]
    public void NewPlayer_HasNoBadges()
    {
        var badges = PlayerBadgeRules.Calculate(0, 0, null);

        Assert.Empty(badges);
    }

    [Fact]
    public void Milestones_UnlockExpectedBadges()
    {
        var now = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

        var badges = PlayerBadgeRules.Calculate(500, 3, now.AddDays(-2), now);

        Assert.Equal(
            ["first-step", "century", "master-builder", "event-veteran", "active-builder"],
            badges.Select(badge => badge.Key));
    }

    [Fact]
    public void OldContribution_DoesNotUnlockActiveBuilder()
    {
        var now = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

        var badges = PlayerBadgeRules.Calculate(1, 1, now.AddDays(-8), now);

        Assert.DoesNotContain(badges, badge => badge.Key == "active-builder");
    }
}
