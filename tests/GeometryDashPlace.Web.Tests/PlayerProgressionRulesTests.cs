using GeometryDashPlace.Web.Profiles;

namespace GeometryDashPlace.Web.Tests;

public sealed class PlayerProgressionRulesTests
{
    [Theory]
    [InlineData(0, 1, 0, 0, 100)]
    [InlineData(9, 1, 90, 90, 100)]
    [InlineData(10, 2, 100, 0, 200)]
    [InlineData(29, 2, 290, 190, 200)]
    [InlineData(30, 3, 300, 0, 300)]
    public void Contributions_CalculateLevelProgress(
        long actions,
        int expectedLevel,
        long expectedTotalXp,
        long expectedCurrentXp,
        long expectedNextXp)
    {
        var progression = PlayerProgressionRules.Calculate(actions, 0);

        Assert.Equal(expectedLevel, progression.Level);
        Assert.Equal(expectedTotalXp, progression.TotalXp);
        Assert.Equal(expectedCurrentXp, progression.CurrentLevelXp);
        Assert.Equal(expectedNextXp, progression.NextLevelXp);
    }

    [Fact]
    public void ClosestLockedBadge_IsSelectedAsNextGoal()
    {
        var progression = PlayerProgressionRules.Calculate(5, 2);

        Assert.NotNull(progression.NextBadge);
        Assert.Equal("event-veteran", progression.NextBadge.Key);
        Assert.Equal(2, progression.NextBadge.Current);
        Assert.Equal(3, progression.NextBadge.Target);
        Assert.Equal(66, progression.NextBadge.ProgressPercent);
    }

    [Fact]
    public void CompletedMilestones_HaveNoNextBadge()
    {
        var progression = PlayerProgressionRules.Calculate(500, 3);

        Assert.Null(progression.NextBadge);
    }

    [Fact]
    public void InvalidNegativeTotals_AreTreatedAsZero()
    {
        var progression = PlayerProgressionRules.Calculate(-12, -4);

        Assert.Equal(1, progression.Level);
        Assert.Equal(0, progression.TotalXp);
        Assert.Equal("first-step", progression.NextBadge?.Key);
    }
}
