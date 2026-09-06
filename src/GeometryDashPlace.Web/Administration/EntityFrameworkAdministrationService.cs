using System.Data;
using System.Text.RegularExpressions;
using GeometryDashPlace.Web.Auth;
using GeometryDashPlace.Web.Data;
using GeometryDashPlace.Web.Data.Entities;
using GeometryDashPlace.Web.Events;
using GeometryDashPlace.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GeometryDashPlace.Web.Administration;

public sealed partial class EntityFrameworkAdministrationService(
    IDbContextFactory<GeometryDashPlaceDbContext> contextFactory,
    SiteOwnership siteOwnership,
    EventLifecycleNotifier lifecycleNotifier)
    : IAdministrationService, IEventLifecycleService
{
    public async Task<bool> IsAdminAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Users.AsNoTracking().AnyAsync(
            user => user.Id == userId && user.IsAdmin && !user.IsBanned,
            cancellationToken);
    }

    public async Task<IReadOnlyList<AdminUser>> GetUsersAsync(
        Guid actorUserId,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await RequireAdminAsync(context, actorUserId, cancellationToken);
        var normalizedSearch = search?.Trim();
        var query = context.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var pattern = $"%{normalizedSearch}%";
            query = query.Where(user =>
                EF.Functions.ILike(user.DisplayName, pattern) ||
                EF.Functions.ILike(user.Email, pattern));
        }

        var users = await query
            .OrderByDescending(user => user.IsAdmin)
            .ThenBy(user => user.DisplayName)
            .Take(50)
            .Select(user => new
            {
                user.Id,
                user.Email,
                user.DisplayName,
                user.IsAdmin,
                user.IsBanned,
                user.LastLoginAt
            })
            .ToListAsync(cancellationToken);
        return users.Select(user => new AdminUser(
            user.Id,
            user.Email,
            user.DisplayName,
            user.IsAdmin,
            siteOwnership.IsOwner(user.Email),
            user.IsBanned,
            user.LastLoginAt)).ToList();
    }

    public async Task<IReadOnlyList<LevelEvent>> GetEventsAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await RequireAdminAsync(context, actorUserId, cancellationToken);
        return await context.Events
            .AsNoTracking()
            .OrderByDescending(levelEvent => levelEvent.StartsAt)
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

    public async Task<LevelEvent> CreateEventAsync(
        Guid actorUserId,
        AdminEventInput input,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeAndValidate(input);
        if (normalized.EndsAt <= DateTimeOffset.UtcNow)
        {
            throw Error(
                "event_already_ended", "A new event must end in the future.",
                StatusCodes.Status400BadRequest);
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await RequireAdminAsync(context, actorUserId, cancellationToken);
        await EnsureScheduleAvailableAsync(context, null, normalized, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var entity = new LevelEventEntity
        {
            Id = Guid.NewGuid(),
            Slug = normalized.Slug,
            Name = normalized.Name,
            Description = normalized.Description,
            Width = 1024,
            Height = 32,
            CooldownSeconds = normalized.CooldownSeconds,
            Status = "open",
            StartsAt = normalized.StartsAt,
            EndsAt = normalized.EndsAt,
            CreatedAt = now,
            UpdatedAt = now
        };
        context.Events.Add(entity);
        await SaveAsync(context, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await lifecycleNotifier.PublishAsync();
        return ToContract(entity);
    }

    public async Task<LevelEvent> UpdateEventAsync(
        Guid actorUserId,
        Guid eventId,
        AdminEventInput input,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeAndValidate(input);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await RequireAdminAsync(context, actorUserId, cancellationToken);
        var entity = await context.Events.SingleOrDefaultAsync(
            levelEvent => levelEvent.Id == eventId,
            cancellationToken) ?? throw Error(
                "event_not_found", "The event does not exist.",
                StatusCodes.Status404NotFound);
        var now = DateTimeOffset.UtcNow;
        var isCompleted = IsCompleted(entity, now);
        if (isCompleted)
        {
            throw Error(
                "completed_event_locked",
                "Only the name, slug and description can be changed after an event is completed.",
                StatusCodes.Status409Conflict);
        }

        if (entity.StartsAt <= now && normalized.StartsAt != entity.StartsAt)
        {
            throw Error(
                "event_start_locked",
                "The start date cannot be changed after the event has started.",
                StatusCodes.Status409Conflict);
        }

        entity.Slug = normalized.Slug;
        entity.Name = normalized.Name;
        entity.Description = normalized.Description;
        entity.CooldownSeconds = normalized.CooldownSeconds;
        entity.StartsAt = normalized.StartsAt;
        entity.EndsAt = normalized.EndsAt;
        if (entity.EndsAt <= now)
        {
            entity.Status = "closed";
            await EnsureFinalSnapshotAsync(context, entity, now, cancellationToken);
        }
        else
        {
            await EnsureScheduleAvailableAsync(
                context, eventId, normalized, cancellationToken);
        }

        await SaveAsync(context, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await lifecycleNotifier.PublishAsync();
        return ToContract(entity);
    }

    public async Task<LevelEvent> UpdateCompletedEventDetailsAsync(
        Guid actorUserId,
        Guid eventId,
        AdminEventDetailsInput input,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeAndValidateDetails(input);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await RequireAdminAsync(context, actorUserId, cancellationToken);
        var entity = await context.Events.SingleOrDefaultAsync(
            levelEvent => levelEvent.Id == eventId,
            cancellationToken) ?? throw Error(
                "event_not_found", "The event does not exist.",
                StatusCodes.Status404NotFound);
        if (!IsCompleted(entity, DateTimeOffset.UtcNow))
        {
            throw Error(
                "event_not_completed", "This event is not completed.",
                StatusCodes.Status409Conflict);
        }

        entity.Slug = normalized.Slug;
        entity.Name = normalized.Name;
        entity.Description = normalized.Description;
        await SaveAsync(context, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToContract(entity);
    }

    public async Task SetAdminAsync(
        Guid actorUserId,
        Guid userId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await RequireAdminAsync(context, actorUserId, cancellationToken);
        var user = await context.Users.SingleOrDefaultAsync(
            candidate => candidate.Id == userId,
            cancellationToken) ?? throw Error(
                "user_not_found", "The user does not exist.",
                StatusCodes.Status404NotFound);

        if (isAdmin && user.IsBanned)
        {
            throw Error(
                "banned_user", "A banned user cannot become an administrator.",
                StatusCodes.Status409Conflict);
        }

        if (!isAdmin)
        {
            if (siteOwnership.IsOwner(user.Email))
            {
                throw Error(
                    "site_owner", "The site owner cannot be removed from the administrators.",
                    StatusCodes.Status409Conflict);
            }

            if (user.Id == actorUserId)
            {
                throw Error(
                    "self_demotion", "You cannot remove your own administrator access.",
                    StatusCodes.Status409Conflict);
            }

            if (user.IsAdmin && await context.Users.CountAsync(
                    candidate => candidate.IsAdmin && !candidate.IsBanned,
                    cancellationToken) <= 1)
            {
                throw Error(
                    "last_admin", "The last administrator cannot be removed.",
                    StatusCodes.Status409Conflict);
            }
        }

        user.IsAdmin = isAdmin;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> CloseExpiredEventsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var expired = await context.Events
            .Where(levelEvent =>
                levelEvent.Status == "open" &&
                levelEvent.EndsAt != null &&
                levelEvent.EndsAt <= now)
            .ToListAsync(cancellationToken);
        foreach (var levelEvent in expired)
        {
            levelEvent.Status = "closed";
            await EnsureFinalSnapshotAsync(context, levelEvent, now, cancellationToken);
        }

        if (expired.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken);
        }

        return expired.Count;
    }

    private static async Task RequireAdminAsync(
        GeometryDashPlaceDbContext context,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!await context.Users.AsNoTracking().AnyAsync(
                user => user.Id == userId && user.IsAdmin && !user.IsBanned,
                cancellationToken))
        {
            throw Error(
                "admin_required", "Administrator access is required.",
                StatusCodes.Status403Forbidden);
        }
    }

    private static AdminEventInput NormalizeAndValidate(AdminEventInput input)
    {
        var details = NormalizeAndValidateDetails(new AdminEventDetailsInput(
            input.Slug, input.Name, input.Description));
        var normalized = input with
        {
            Slug = details.Slug,
            Name = details.Name,
            Description = details.Description,
            StartsAt = input.StartsAt?.ToUniversalTime(),
            EndsAt = input.EndsAt?.ToUniversalTime()
        };

        if (normalized.CooldownSeconds is < 0 or > 86400)
        {
            throw Error(
                "invalid_cooldown", "The cooldown must be between 0 and 86400 seconds.",
                StatusCodes.Status400BadRequest);
        }

        if (normalized.StartsAt is not { } startsAt ||
            normalized.EndsAt is not { } endsAt ||
            endsAt <= startsAt)
        {
            throw Error(
                "invalid_dates", "A start date and a later end date are required.",
                StatusCodes.Status400BadRequest);
        }

        return normalized;
    }

    private static AdminEventDetailsInput NormalizeAndValidateDetails(
        AdminEventDetailsInput input)
    {
        var normalized = input with
        {
            Slug = input.Slug.Trim().ToLowerInvariant(),
            Name = input.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(input.Description)
                ? null
                : input.Description.Trim()
        };
        if (normalized.Slug.Length is 0 or > 80 ||
            !SlugPattern().IsMatch(normalized.Slug))
        {
            throw Error(
                "invalid_slug", "The slug must use lowercase letters, numbers and single hyphens.",
                StatusCodes.Status400BadRequest);
        }

        if (normalized.Name.Length is 0 or > 120)
        {
            throw Error(
                "invalid_name", "The event name must contain between 1 and 120 characters.",
                StatusCodes.Status400BadRequest);
        }

        if (normalized.Description?.Length > 2000)
        {
            throw Error(
                "invalid_description", "The description cannot exceed 2000 characters.",
                StatusCodes.Status400BadRequest);
        }

        return normalized;
    }

    private static async Task EnsureScheduleAvailableAsync(
        GeometryDashPlaceDbContext context,
        Guid? eventId,
        AdminEventInput input,
        CancellationToken cancellationToken)
    {
        var overlap = await context.Events.AsNoTracking().AnyAsync(
            levelEvent =>
                levelEvent.Id != eventId &&
                levelEvent.Status == "open" &&
                (input.EndsAt == null ||
                 levelEvent.StartsAt == null ||
                 levelEvent.StartsAt < input.EndsAt) &&
                (input.StartsAt == null ||
                 levelEvent.EndsAt == null ||
                 levelEvent.EndsAt > input.StartsAt),
            cancellationToken);
        if (overlap)
        {
            throw Error(
                "event_schedule_overlap",
                "This schedule overlaps another open or scheduled event.",
                StatusCodes.Status409Conflict);
        }
    }

    private static async Task EnsureFinalSnapshotAsync(
        GeometryDashPlaceDbContext context,
        LevelEventEntity levelEvent,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var cells = await context.LevelCells
            .AsNoTracking()
            .Include(cell => cell.Author)
            .Where(cell => cell.EventId == levelEvent.Id)
            .OrderBy(cell => cell.X)
            .ThenBy(cell => cell.Y)
            .Select(cell => new LevelCell(
                cell.X,
                cell.Y,
                cell.ObjectTypeKey,
                (double)cell.Rotation,
                (double)cell.ScaleX,
                (double)cell.ScaleY,
                cell.ColorRed,
                cell.ColorGreen,
                cell.ColorBlue,
                cell.DurationSeconds == null ? null : (double?)cell.DurationSeconds,
                cell.AuthorUserId,
                cell.Author.DisplayName,
                cell.Revision,
                cell.PlacedAt))
            .ToListAsync(cancellationToken);
        var snapshot = await context.LevelSnapshots.SingleOrDefaultAsync(
            candidate =>
                candidate.EventId == levelEvent.Id &&
                candidate.Revision == levelEvent.CurrentRevision,
            cancellationToken);
        if (snapshot is null)
        {
            context.LevelSnapshots.Add(new LevelSnapshotEntity
            {
                EventId = levelEvent.Id,
                Revision = levelEvent.CurrentRevision,
                SnapshotType = "final",
                State = cells,
                CreatedAt = now
            });
        }
        else
        {
            snapshot.SnapshotType = "final";
            snapshot.State = cells;
            snapshot.CreatedAt = now;
        }
    }

    private static async Task SaveAsync(
        GeometryDashPlaceDbContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw Error(
                "slug_already_exists", "Another event already uses this slug.",
                StatusCodes.Status409Conflict);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Error(
                "event_changed", "The event changed concurrently. Reload and try again.",
                StatusCodes.Status409Conflict);
        }
    }

    private static bool IsUniqueViolation(Exception exception) =>
        exception is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        } || exception.InnerException is not null && IsUniqueViolation(exception.InnerException);

    private static LevelEvent ToContract(LevelEventEntity levelEvent) => new(
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
        levelEvent.EndsAt);

    private static bool IsCompleted(
        LevelEventEntity levelEvent,
        DateTimeOffset now) =>
        levelEvent.Status is "closed" or "archived" ||
        levelEvent.EndsAt <= now;

    private static AdministrationException Error(
        string code,
        string message,
        int statusCode) => new(code, message, statusCode);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
