namespace GeometryDashPlace.Web.Profiles;

public sealed record PlayerBadgeProgress(
    string Key,
    string Name,
    long Current,
    long Target,
    string Unit,
    int ProgressPercent);

public sealed record PlayerProgression(
    int Level,
    long TotalXp,
    long CurrentLevelXp,
    long NextLevelXp,
    int LevelProgressPercent,
    PlayerBadgeProgress? NextBadge);

public static class PlayerProgressionRules
{
    public const int XpPerContribution = 10;
    public const int MaximumLevel = 100;

    private static readonly IReadOnlyList<BadgeGoal> BadgeGoals =
    [
        new("first-step", "FIRST STEP", 1, "CONTRIBUTION", ProgressSource.Actions),
        new("builder-25", "BUILDER", 25, "CONTRIBUTIONS", ProgressSource.Actions),
        new("century", "CENTURY", 100, "CONTRIBUTIONS", ProgressSource.Actions),
        new("master-builder", "MASTER BUILDER", 500, "CONTRIBUTIONS", ProgressSource.Actions),
        new("event-veteran", "EVENT VETERAN", 3, "EVENTS", ProgressSource.Events)
    ];

    public static PlayerProgression Calculate(long totalActions, int eventCount)
    {
        var safeActions = Math.Max(0, totalActions);
        var safeEventCount = Math.Max(0, eventCount);
        var totalXp = safeActions > long.MaxValue / XpPerContribution
            ? long.MaxValue
            : safeActions * XpPerContribution;

        var level = 1;
        while (level < MaximumLevel && totalXp >= TotalXpAtLevel(level + 1))
        {
            level++;
        }

        var levelStartXp = TotalXpAtLevel(level);
        var nextLevelXp = level == MaximumLevel
            ? 0
            : TotalXpAtLevel(level + 1) - levelStartXp;
        var currentLevelXp = level == MaximumLevel
            ? 0
            : totalXp - levelStartXp;
        var levelProgress = nextLevelXp == 0
            ? 100
            : Percentage(currentLevelXp, nextLevelXp);

        var nextBadge = BadgeGoals
            .Select((goal, index) => ToProgress(goal, index, safeActions, safeEventCount))
            .Where(candidate => candidate.Progress.Current < candidate.Progress.Target)
            .OrderByDescending(candidate => candidate.Progress.ProgressPercent)
            .ThenBy(candidate => candidate.Index)
            .Select(candidate => candidate.Progress)
            .FirstOrDefault();

        return new PlayerProgression(
            level,
            totalXp,
            currentLevelXp,
            nextLevelXp,
            levelProgress,
            nextBadge);
    }

    public static long TotalXpAtLevel(int level)
    {
        var safeLevel = Math.Clamp(level, 1, MaximumLevel);
        return 50L * safeLevel * (safeLevel - 1);
    }

    private static BadgeCandidate ToProgress(
        BadgeGoal goal,
        int index,
        long totalActions,
        int eventCount)
    {
        var current = goal.Source == ProgressSource.Actions
            ? Math.Min(totalActions, goal.Target)
            : Math.Min(eventCount, goal.Target);
        return new BadgeCandidate(
            index,
            new PlayerBadgeProgress(
                goal.Key,
                goal.Name,
                current,
                goal.Target,
                goal.Unit,
                Percentage(current, goal.Target)));
    }

    private static int Percentage(long current, long target) =>
        target <= 0 ? 100 : (int)Math.Clamp(current * 100 / target, 0, 100);

    private enum ProgressSource
    {
        Actions,
        Events
    }

    private sealed record BadgeGoal(
        string Key,
        string Name,
        long Target,
        string Unit,
        ProgressSource Source);

    private sealed record BadgeCandidate(int Index, PlayerBadgeProgress Progress);
}
