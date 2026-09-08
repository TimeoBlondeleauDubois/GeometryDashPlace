using GeometryDashPlace.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace GeometryDashPlace.Web.Events;

public sealed class EntityFrameworkEventResultsRepository(
    IDbContextFactory<GeometryDashPlaceDbContext> contextFactory) : IEventResultsRepository
{
    public async Task<EventResultSummary?> GetBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var levelEvent = await context.Events
            .AsNoTracking()
            .Where(candidate =>
                candidate.Slug == slug &&
                candidate.Status != "draft" &&
                (candidate.Status == "closed" ||
                 candidate.Status == "archived" ||
                 candidate.EndsAt != null && candidate.EndsAt <= now))
            .Select(candidate => new LevelEvent(
                candidate.Id,
                candidate.Slug,
                candidate.Name,
                candidate.Description,
                candidate.Width,
                candidate.Height,
                candidate.CooldownSeconds,
                candidate.CurrentRevision,
                candidate.Status,
                candidate.StartsAt,
                candidate.EndsAt,
                candidate.BackgroundKey,
                candidate.GroundKey))
            .SingleOrDefaultAsync(cancellationToken);
        if (levelEvent is null)
        {
            return null;
        }

        var actionRows = await context.PlacementHistory
            .AsNoTracking()
            .Where(history => history.EventId == levelEvent.Id)
            .GroupBy(history => history.Action)
            .Select(group => new ActionRow(group.Key, group.LongCount()))
            .ToListAsync(cancellationToken);
        var contributorRows = await context.PlacementHistory
            .AsNoTracking()
            .Where(history => history.EventId == levelEvent.Id)
            .GroupBy(history => new
            {
                history.UserId,
                history.User.DisplayName,
                history.Action
            })
            .Select(group => new ContributorActionRow(
                group.Key.UserId,
                group.Key.DisplayName,
                group.Key.Action,
                group.LongCount(),
                group.Min(history => history.PlacedAt),
                group.Max(history => history.PlacedAt)))
            .ToListAsync(cancellationToken);
        var finalObjectCount = await context.LevelCells
            .AsNoTracking()
            .CountAsync(cell => cell.EventId == levelEvent.Id, cancellationToken);

        var contributors = contributorRows
            .GroupBy(row => new { row.UserId, row.DisplayName })
            .Select(group => new EventContributorResult(
                group.Key.UserId,
                group.Key.DisplayName,
                Totals(group.Select(row => new ActionRow(row.Action, row.Count))),
                group.Min(row => row.FirstContributionAt),
                group.Max(row => row.LastContributionAt)))
            .OrderByDescending(contributor => contributor.Actions.Total)
            .ThenBy(contributor => contributor.DisplayName)
            .ToArray();

        return new EventResultSummary(
            levelEvent,
            finalObjectCount,
            Totals(actionRows),
            contributorRows.Count == 0
                ? null
                : contributorRows.Min(row => row.FirstContributionAt),
            contributorRows.Count == 0
                ? null
                : contributorRows.Max(row => row.LastContributionAt),
            contributors);
    }

    private static EventActionTotals Totals(IEnumerable<ActionRow> rows)
    {
        var counts = rows.ToDictionary(row => row.Action, row => row.Count);
        return new EventActionTotals(
            Count("place"),
            Count("replace"),
            Count("delete"),
            Count("move"),
            Count("move_replace"));

        long Count(string action) => counts.GetValueOrDefault(action);
    }

    private sealed record ActionRow(string Action, long Count);

    private sealed record ContributorActionRow(
        Guid UserId,
        string DisplayName,
        string Action,
        long Count,
        DateTimeOffset FirstContributionAt,
        DateTimeOffset LastContributionAt);
}
