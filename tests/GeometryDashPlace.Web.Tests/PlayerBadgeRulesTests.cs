using GeometryDashPlace.Web.Profiles;

namespace GeometryDashPlace.Web.Tests;

public sealed class PlayerBadgeRulesTests
{
    [Fact]
    public void NewPlayer_HasNoBadges()
    {
        var badges = PlayerBadgeRules.Calculate(0, 0);

        Assert.Empty(badges);
    }

    [Fact]
    public void Milestones_UnlockExpectedBadges()
    {
        var badges = PlayerBadgeRules.Calculate(500, 3);

        Assert.Equal(
            ["first-step", "builder-25", "century", "master-builder", "event-veteran"],
            badges.Select(badge => badge.Key));
    }

    [Fact]
    public void BuilderBadge_UnlocksAtTwentyFiveContributions()
    {
        var before = PlayerBadgeRules.Calculate(24, 1);
        var atThreshold = PlayerBadgeRules.Calculate(25, 1);

        Assert.DoesNotContain(before, badge => badge.Key == "builder-25");
        Assert.Contains(atThreshold, badge => badge.Key == "builder-25");
    }
}
