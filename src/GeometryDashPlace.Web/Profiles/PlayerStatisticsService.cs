using GeometryDashPlace.Web.Data;
using GeometryDashPlace.Web.Data.Entities;
using GeometryDashPlace.Web.Events;
using Microsoft.EntityFrameworkCore;

namespace GeometryDashPlace.Web.Profiles;

public sealed record PlayerEventContribution(
    string EventSlug,
    string EventName,
    long Rank,
    EventActionTotals Actions,
    DateTimeOffset FirstContributionAt,
    DateTimeOffset LastContributionAt);

public sealed record PlayerRecentActivity(
    string EventSlug,
    string EventName,
    string Action,
    string? ObjectType,
    int X,
    int Y,
    DateTimeOffset OccurredAt);

public sealed record PlayerBadge(
    string Key,
    string Name,
    string Description,
    DateTimeOffset? UnlockedAt = null,
    string? EventSlug = null,
    string? EventName = null);

public sealed record PublicPlayerProfile(
    Guid UserId,
    string Username,
    string? AvatarUrl,
    DateTimeOffset JoinedAt,
    long GlobalRank,
    EventActionTotals Actions,
    PlayerProgression Progression,
    IReadOnlyList<PlayerEventContribution> Events,
    IReadOnlyList<PlayerRecentActivity> RecentActivity,
    IReadOnlyList<PlayerBadge> Badges);

public sealed record LeaderboardEventOption(string Slug, string Name);

public sealed record PlayerLeaderboardEntry(
    long Rank,
    Guid UserId,
    string Username,
    string? AvatarUrl,
    int Level,
    long TotalXp,
    EventActionTotals Actions,
    int EventCount,
    DateTimeOffset LastContributionAt);

public sealed record PlayerLeaderboard(
    string? SelectedEventSlug,
    string? SelectedEventName,
    IReadOnlyList<LeaderboardEventOption> Events,
    IReadOnlyList<PlayerLeaderboardEntry> Entries);

public interface IPlayerStatisticsService
{
    Task<PublicPlayerProfile?> GetProfileAsync(
        string username,
        CancellationToken cancellationToken = default);

    Task<PlayerLeaderboard?> GetLeaderboardAsync(
        string? eventSlug,
        int limit = 100,
        CancellationToken cancellationToken = default);
}

public sealed class PlayerStatisticsService(
    IDbContextFactory<GeometryDashPlaceDbContext> contextFactory,
    IPlayerBadgeService badgeService) : IPlayerStatisticsService
{
    public async Task<PublicPlayerProfile?> GetProfileAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        if (UserProfileRules.ValidateUsername(username) is not null)
        {
            return null;
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var normalizedUsername = UserProfileRules.NormalizeUsername(username);
        var user = await context.Users
            .AsNoTracking()
            .Where(candidate => candidate.NormalizedUsername == normalizedUsername &&
                                candidate.IsProfileCompleted &&
                                !candidate.IsBanned)
            .Select(candidate => new PublicUserRow(
                candidate.Id,
                candidate.Username!,
                candidate.AvatarUrl,
                candidate.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return null;
        }

        var contributionRows = await context.PlacementHistory
            .AsNoTracking()
            .Where(history => history.UserId == user.Id)
            .GroupBy(history => new
            {
                history.EventId,
                history.Event.Slug,
                history.Event.Name
            })
            .Select(group => new ContributionRow(
                group.Key.EventId,
                group.Key.Slug,
                group.Key.Name,
                group.LongCount(history => history.Action == "place"),
                group.LongCount(history => history.Action == "replace"),
                group.LongCount(history => history.Action == "delete"),
                group.LongCount(history => history.Action == "move"),
                group.LongCount(history => history.Action == "move_replace"),
                group.Min(history => history.PlacedAt),
                group.Max(history => history.PlacedAt)))
            .ToListAsync(cancellationToken);

        var recentRows = await context.PlacementHistory
            .AsNoTracking()
            .Include(history => history.Event)
            .Where(history => history.UserId == user.Id)
            .OrderByDescending(history => history.PlacedAt)
            .ThenByDescending(history => history.Revision)
            .Take(20)
            .ToListAsync(cancellationToken);

        var allContributorTotals = await context.PlacementHistory
            .AsNoTracking()
            .Where(history => history.User.IsProfileCompleted && !history.User.IsBanned)
            .GroupBy(history => history.UserId)
            .OrderByDescending(group => group.LongCount())
            .ThenBy(group => group.Key)
            .Select(group => new RankedTotalRow(group.Key, group.LongCount()))
            .ToListAsync(cancellationToken);
        var globalRank = RankOf(allContributorTotals, user.Id);

        var eventIds = contributionRows.Select(row => row.EventId).ToList();
        var eventTotals = eventIds.Count == 0
            ? []
            : await context.PlacementHistory
                .AsNoTracking()
                .Where(history => eventIds.Contains(history.EventId) &&
                                  history.User.IsProfileCompleted &&
                                  !history.User.IsBanned)
                .GroupBy(history => new { history.EventId, history.UserId })
                .Select(group => new RankedEventTotalRow(
                    group.Key.EventId,
                    group.Key.UserId,
                    group.LongCount()))
                .ToListAsync(cancellationToken);

        var contributions = contributionRows
            .Select(row => new PlayerEventContribution(
                row.EventSlug,
                row.EventName,
                RankOf(eventTotals.Where(candidate => candidate.EventId == row.EventId), user.Id),
                ToTotals(row),
                row.FirstContributionAt,
                row.LastContributionAt))
            .OrderByDescending(contribution => contribution.LastContributionAt)
            .ToArray();
        var actions = SumTotals(contributions.Select(contribution => contribution.Actions));
        var recent = recentRows.Select(history => new PlayerRecentActivity(
            history.Event.Slug,
            history.Event.Name,
            history.Action,
            history.NewObject?.Type ?? history.PreviousObject?.Type,
            history.X,
            history.Y,
            history.PlacedAt)).ToArray();

        var badges = await badgeService.GetPublicBadgesAsync(user.Id, cancellationToken);
        var progression = PlayerProgressionRules.Calculate(actions.Total, contributions.Length);
        return new PublicPlayerProfile(
            user.Id,
            user.Username,
            user.AvatarUrl,
            user.JoinedAt,
            globalRank,
            actions,
            progression,
            contributions,
            recent,
            badges);
    }

    public async Task<PlayerLeaderboard?> GetLeaderboardAsync(
        string? eventSlug,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var events = await context.Events
            .AsNoTracking()
            .Where(levelEvent => levelEvent.Status != "draft")
            .OrderByDescending(levelEvent => levelEvent.StartsAt ?? levelEvent.CreatedAt)
            .Select(levelEvent => new LeaderboardEventOption(levelEvent.Slug, levelEvent.Name))
            .ToListAsync(cancellationToken);

        Guid? eventId = null;
        string? selectedEventName = null;
        if (!string.IsNullOrWhiteSpace(eventSlug))
        {
            var selectedEvent = await context.Events
                .AsNoTracking()
                .Where(levelEvent => levelEvent.Slug == eventSlug && levelEvent.Status != "draft")
                .Select(levelEvent => new { levelEvent.Id, levelEvent.Name })
                .SingleOrDefaultAsync(cancellationToken);
            if (selectedEvent is null)
            {
                return null;
            }

            eventId = selectedEvent.Id;
            selectedEventName = selectedEvent.Name;
        }

        var historyQuery = context.PlacementHistory
            .AsNoTracking()
            .Where(history => history.User.IsProfileCompleted &&
                              !history.User.IsBanned &&
                              history.User.Username != null);
        if (eventId is { } selectedEventId)
        {
            historyQuery = historyQuery.Where(history => history.EventId == selectedEventId);
        }

        var rows = await historyQuery
            .GroupBy(history => new
            {
                history.UserId,
                Username = history.User.Username!,
                history.User.AvatarUrl
            })
            .OrderByDescending(group => group.LongCount())
            .ThenBy(group => group.Key.Username)
            .Select(group => new LeaderboardRow(
                group.Key.UserId,
                group.Key.Username,
                group.Key.AvatarUrl,
                group.LongCount(),
                group.LongCount(history => history.Action == "place"),
                group.LongCount(history => history.Action == "replace"),
                group.LongCount(history => history.Action == "delete"),
                group.LongCount(history => history.Action == "move"),
                group.LongCount(history => history.Action == "move_replace"),
                group.Select(history => history.EventId).Distinct().Count(),
                group.Max(history => history.PlacedAt)))
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);

        Dictionary<Guid, ProgressionTotalRow>? globalProgression = null;
        if (eventId is not null && rows.Count > 0)
        {
            var rankedUserIds = rows.Select(row => row.UserId).ToArray();
            globalProgression = await context.PlacementHistory
                .AsNoTracking()
                .Where(history => rankedUserIds.Contains(history.UserId))
                .GroupBy(history => history.UserId)
                .Select(group => new ProgressionTotalRow(
                    group.Key,
                    group.LongCount(),
                    group.Select(history => history.EventId).Distinct().Count()))
                .ToDictionaryAsync(row => row.UserId, cancellationToken);
        }

        var entries = rows.Select((row, index) =>
        {
            var totals = globalProgression is not null &&
                         globalProgression.TryGetValue(row.UserId, out var global)
                ? global
                : new ProgressionTotalRow(row.UserId, row.Total, row.EventCount);
            var progression = PlayerProgressionRules.Calculate(
                totals.TotalActions, totals.EventCount);
            return new PlayerLeaderboardEntry(
                index + 1,
                row.UserId,
                row.Username,
                row.AvatarUrl,
                progression.Level,
                progression.TotalXp,
                ToTotals(row),
                row.EventCount,
                row.LastContributionAt);
        }).ToArray();
        return new PlayerLeaderboard(eventSlug, selectedEventName, events, entries);
    }

    private static long RankOf(IEnumerable<RankedTotalRow> rows, Guid userId)
    {
        var ordered = rows.OrderByDescending(row => row.Total).ThenBy(row => row.UserId).ToArray();
        var index = Array.FindIndex(ordered, row => row.UserId == userId);
        return index < 0 ? 0 : index + 1;
    }

    private static long RankOf(IEnumerable<RankedEventTotalRow> rows, Guid userId) =>
        RankOf(rows.Select(row => new RankedTotalRow(row.UserId, row.Total)), userId);

    private static EventActionTotals ToTotals(IActionCounts row) => new(
        row.Placements,
        row.Replacements,
        row.Deletions,
        row.Moves,
        row.MoveReplacements);

    private static EventActionTotals SumTotals(IEnumerable<EventActionTotals> totals) =>
        totals.Aggregate(
            new EventActionTotals(0, 0, 0, 0, 0),
            (sum, value) => new EventActionTotals(
                sum.Placements + value.Placements,
                sum.Replacements + value.Replacements,
                sum.Deletions + value.Deletions,
                sum.Moves + value.Moves,
                sum.MoveReplacements + value.MoveReplacements));

    private interface IActionCounts
    {
        long Placements { get; }
        long Replacements { get; }
        long Deletions { get; }
        long Moves { get; }
        long MoveReplacements { get; }
    }

    private sealed record PublicUserRow(
        Guid Id,
        string Username,
        string? AvatarUrl,
        DateTimeOffset JoinedAt);

    private sealed record ContributionRow(
        Guid EventId,
        string EventSlug,
        string EventName,
        long Placements,
        long Replacements,
        long Deletions,
        long Moves,
        long MoveReplacements,
        DateTimeOffset FirstContributionAt,
        DateTimeOffset LastContributionAt) : IActionCounts;

    private sealed record LeaderboardRow(
        Guid UserId,
        string Username,
        string? AvatarUrl,
        long Total,
        long Placements,
        long Replacements,
        long Deletions,
        long Moves,
        long MoveReplacements,
        int EventCount,
        DateTimeOffset LastContributionAt) : IActionCounts;

    private sealed record RankedTotalRow(Guid UserId, long Total);
    private sealed record RankedEventTotalRow(Guid EventId, Guid UserId, long Total);
    private sealed record ProgressionTotalRow(Guid UserId, long TotalActions, int EventCount);
}

public static class PlayerBadgeRules
{
    private static readonly IReadOnlyList<PlayerBadge> Definitions =
    [
        new("first-step", "FIRST STEP", "Made a first contribution."),
        new("builder-25", "BUILDER", "Reached 25 contributions."),
        new("century", "CENTURY", "Reached 100 contributions."),
        new("master-builder", "MASTER BUILDER", "Reached 500 contributions."),
        new("event-veteran", "EVENT VETERAN", "Contributed to at least 3 events."),
        new("event-top-three", "EVENT TOP 3", "Finished in the top 3 of an event.")
    ];

    public static IReadOnlyList<PlayerBadge> Calculate(long totalActions, int eventCount)
    {
        var badges = new List<PlayerBadge>();
        if (totalActions >= 1)
        {
            badges.Add(Definitions[0]);
        }

        if (totalActions >= 25)
        {
            badges.Add(Definitions[1]);
        }

        if (totalActions >= 100)
        {
            badges.Add(Definitions[2]);
        }

        if (totalActions >= 500)
        {
            badges.Add(Definitions[3]);
        }

        if (eventCount >= 3)
        {
            badges.Add(Definitions[4]);
        }

        return badges;
    }

    public static PlayerBadge? Find(string key) =>
        Definitions.FirstOrDefault(badge => badge.Key == key);
}
