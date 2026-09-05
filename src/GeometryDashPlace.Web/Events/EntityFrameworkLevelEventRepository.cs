using GeometryDashPlace.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace GeometryDashPlace.Web.Events;

public sealed class EntityFrameworkLevelEventRepository(
    IDbContextFactory<GeometryDashPlaceDbContext> contextFactory) : ILevelEventRepository
{
    public async Task<LevelEvent?> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        return await context.Events
            .AsNoTracking()
            .Where(levelEvent =>
                levelEvent.Status == "open" &&
                (levelEvent.StartsAt == null || levelEvent.StartsAt <= now) &&
                (levelEvent.EndsAt == null || levelEvent.EndsAt > now))
            .OrderBy(levelEvent => levelEvent.StartsAt == null)
            .ThenByDescending(levelEvent => levelEvent.StartsAt)
            .ThenByDescending(levelEvent => levelEvent.CreatedAt)
            .Select(levelEvent => new LevelEvent(
                levelEvent.Id,
                levelEvent.Slug,
                levelEvent.Name,
                levelEvent.Description,
                levelEvent.Width,
                levelEvent.Height,
                levelEvent.CooldownSeconds,
                levelEvent.CurrentRevision,
                levelEvent.Status,
                levelEvent.StartsAt,
                levelEvent.EndsAt))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LevelEvent>> GetPastAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        return await context.Events
            .AsNoTracking()
            .Where(levelEvent =>
                levelEvent.Status != "draft" &&
                (levelEvent.Status == "closed" ||
                 levelEvent.Status == "archived" ||
                 levelEvent.EndsAt != null && levelEvent.EndsAt <= now))
            .OrderByDescending(levelEvent => levelEvent.EndsAt)
            .ThenByDescending(levelEvent => levelEvent.CreatedAt)
            .Select(levelEvent => new LevelEvent(
                levelEvent.Id,
                levelEvent.Slug,
                levelEvent.Name,
                levelEvent.Description,
                levelEvent.Width,
                levelEvent.Height,
                levelEvent.CooldownSeconds,
                levelEvent.CurrentRevision,
                levelEvent.Status,
                levelEvent.StartsAt,
                levelEvent.EndsAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<LevelEvent?> GetBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Events
            .AsNoTracking()
            .Where(levelEvent =>
                levelEvent.Slug == slug &&
                levelEvent.Status != "draft")
            .Select(levelEvent => new LevelEvent(
                levelEvent.Id,
                levelEvent.Slug,
                levelEvent.Name,
                levelEvent.Description,
                levelEvent.Width,
                levelEvent.Height,
                levelEvent.CooldownSeconds,
                levelEvent.CurrentRevision,
                levelEvent.Status,
                levelEvent.StartsAt,
                levelEvent.EndsAt))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
