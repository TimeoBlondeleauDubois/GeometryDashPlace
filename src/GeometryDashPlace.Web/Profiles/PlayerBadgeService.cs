using GeometryDashPlace.Web.Data;
using GeometryDashPlace.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GeometryDashPlace.Web.Profiles;

public sealed record PlayerBadgeNotification(
    string Key,
    string ScopeKey,
    string Name,
    string Description,
    DateTimeOffset UnlockedAt);

public sealed record PlayerBadgeReference(string Key, string ScopeKey);

public interface IPlayerBadgeService
{
    Task<IReadOnlyList<PlayerBadgeNotification>> GetPendingAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task MarkSeenAsync(
        Guid userId,
        IReadOnlyCollection<PlayerBadgeReference> badges,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlayerBadge>> GetPublicBadgesAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<int> AwardCompletedEventBadgesAsync(
        CancellationToken cancellationToken = default);
}

public sealed class PlayerBadgeService(
    IDbContextFactory<GeometryDashPlaceDbContext> contextFactory) : IPlayerBadgeService
{
    private const string GlobalScope = "global";

    public async Task<IReadOnlyList<PlayerBadgeNotification>> GetPendingAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await EnsureEligibleBadgesAsync(userId, cancellationToken);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.PlayerBadges
            .AsNoTracking()
            .Where(badge => badge.UserId == userId && badge.SeenAt == null)
            .OrderBy(badge => badge.UnlockedAt)
            .ThenBy(badge => badge.BadgeKey)
            .Select(badge => new BadgeRow(
                badge.BadgeKey,
                badge.ScopeKey,
                badge.UnlockedAt,
                badge.Event == null ? null : badge.Event.Slug,
                badge.Event == null ? null : badge.Event.Name))
            .ToListAsync(cancellationToken);
        return rows.Select(ToNotification).ToArray();
    }

    public async Task MarkSeenAsync(
        Guid userId,
        IReadOnlyCollection<PlayerBadgeReference> badges,
        CancellationToken cancellationToken = default)
    {
        if (badges.Count == 0)
        {
            return;
        }

        var references = badges
            .Select(badge => BadgeReference(badge.Key, badge.ScopeKey))
            .ToHashSet(StringComparer.Ordinal);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var unseen = await context.PlayerBadges
            .Where(badge => badge.UserId == userId && badge.SeenAt == null)
            .ToListAsync(cancellationToken);
        var seenAt = DateTimeOffset.UtcNow;
        foreach (var badge in unseen.Where(badge =>
                     references.Contains(BadgeReference(badge.BadgeKey, badge.ScopeKey))))
        {
            badge.SeenAt = seenAt;
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlayerBadge>> GetPublicBadgesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await EnsureEligibleBadgesAsync(userId, cancellationToken);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.PlayerBadges
            .AsNoTracking()
            .Where(badge => badge.UserId == userId)
            .OrderBy(badge => badge.UnlockedAt)
            .ThenBy(badge => badge.BadgeKey)
            .Select(badge => new BadgeRow(
                badge.BadgeKey,
                badge.ScopeKey,
                badge.UnlockedAt,
                badge.Event == null ? null : badge.Event.Slug,
                badge.Event == null ? null : badge.Event.Name))
            .ToListAsync(cancellationToken);
        return rows.Select(ToPublicBadge).ToArray();
    }

    public async Task<int> AwardCompletedEventBadgesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var eventIds = await context.Events
            .AsNoTracking()
            .Where(levelEvent =>
                (levelEvent.Status == "closed" || levelEvent.Status == "archived") &&
                levelEvent.PlacementHistory.Any() &&
                !levelEvent.Badges.Any(badge => badge.BadgeKey == "event-top-three"))
            .OrderBy(levelEvent => levelEvent.EndsAt ?? levelEvent.UpdatedAt)
            .Select(levelEvent => levelEvent.Id)
            .Take(20)
            .ToListAsync(cancellationToken);
        if (eventIds.Count == 0)
        {
            return 0;
        }

        var awarded = 0;
        var unlockedAt = DateTimeOffset.UtcNow;
        foreach (var eventId in eventIds)
        {
            var winners = await context.PlacementHistory
                .AsNoTracking()
                .Where(history =>
                    history.EventId == eventId &&
                    history.User.IsProfileCompleted &&
                    !history.User.IsBanned)
                .GroupBy(history => history.UserId)
                .Select(group => new EventRankingRow(
                    group.Key,
                    group.LongCount(),
                    group.Min(history => history.PlacedAt)))
                .OrderByDescending(row => row.TotalActions)
                .ThenBy(row => row.FirstContributionAt)
                .ThenBy(row => row.UserId)
                .Take(3)
                .ToListAsync(cancellationToken);

            foreach (var winner in winners)
            {
                context.PlayerBadges.Add(new PlayerBadgeEntity
                {
                    UserId = winner.UserId,
                    BadgeKey = "event-top-three",
                    ScopeKey = eventId.ToString("N"),
                    EventId = eventId,
                    UnlockedAt = unlockedAt
                });
                awarded++;
            }
        }

        if (awarded == 0)
        {
            return 0;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return awarded;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // A concurrent lifecycle check already awarded these event badges.
            return 0;
        }
    }

    private async Task EnsureEligibleBadgesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var isEligibleUser = await context.Users.AsNoTracking().AnyAsync(
            user => user.Id == userId && user.IsProfileCompleted && !user.IsBanned,
            cancellationToken);
        if (!isEligibleUser)
        {
            return;
        }

        var totals = await context.PlacementHistory
            .AsNoTracking()
            .Where(history => history.UserId == userId)
            .GroupBy(history => history.UserId)
            .Select(group => new PlayerTotals(
                group.LongCount(),
                group.Select(history => history.EventId).Distinct().Count()))
            .SingleOrDefaultAsync(cancellationToken) ?? new PlayerTotals(0, 0);
        var eligibleBadges = PlayerBadgeRules.Calculate(totals.TotalActions, totals.EventCount);
        if (eligibleBadges.Count == 0)
        {
            return;
        }

        var existingKeys = await context.PlayerBadges
            .AsNoTracking()
            .Where(badge => badge.UserId == userId && badge.ScopeKey == GlobalScope)
            .Select(badge => badge.BadgeKey)
            .ToListAsync(cancellationToken);
        var existing = existingKeys.ToHashSet(StringComparer.Ordinal);
        var unlockedAt = DateTimeOffset.UtcNow;
        foreach (var badge in eligibleBadges.Where(badge => !existing.Contains(badge.Key)))
        {
            context.PlayerBadges.Add(new PlayerBadgeEntity
            {
                UserId = userId,
                BadgeKey = badge.Key,
                ScopeKey = GlobalScope,
                UnlockedAt = unlockedAt
            });
        }

        if (!context.ChangeTracker.HasChanges())
        {
            return;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Another simultaneous request already unlocked the same badge.
        }
    }

    private static PlayerBadgeNotification ToNotification(BadgeRow row)
    {
        var badge = PlayerBadgeRules.Find(row.Key);
        return new PlayerBadgeNotification(
            row.Key,
            row.ScopeKey,
            badge?.Name ?? row.Key.Replace('-', ' ').ToUpperInvariant(),
            DescriptionFor(badge, row.EventName),
            row.UnlockedAt);
    }

    private static PlayerBadge ToPublicBadge(BadgeRow row)
    {
        var badge = PlayerBadgeRules.Find(row.Key);
        return new PlayerBadge(
            row.Key,
            badge?.Name ?? row.Key.Replace('-', ' ').ToUpperInvariant(),
            DescriptionFor(badge, row.EventName),
            row.UnlockedAt,
            row.EventSlug,
            row.EventName);
    }

    private static string BadgeReference(string key, string scopeKey) => $"{key}\n{scopeKey}";

    private static string DescriptionFor(PlayerBadge? badge, string? eventName) =>
        badge?.Key == "event-top-three" && !string.IsNullOrWhiteSpace(eventName)
            ? $"Finished in the top 3 of {eventName}."
            : badge?.Description ?? "Unlocked a community achievement.";

    private static bool IsUniqueViolation(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } ||
        exception.InnerException is not null && IsUniqueViolation(exception.InnerException);

    private sealed record PlayerTotals(long TotalActions, int EventCount);

    private sealed record EventRankingRow(
        Guid UserId,
        long TotalActions,
        DateTimeOffset FirstContributionAt);

    private sealed record BadgeRow(
        string Key,
        string ScopeKey,
        DateTimeOffset UnlockedAt,
        string? EventSlug,
        string? EventName);
}
