using System.Data;
using System.Text.RegularExpressions;
using GeometryDashPlace.Web.Auth;
using GeometryDashPlace.Web.Data;
using GeometryDashPlace.Web.Data.Entities;
using GeometryDashPlace.Web.Events;
using GeometryDashPlace.Web.Assets;
using GeometryDashPlace.Web.Persistence;
using GeometryDashPlace.Web.Realtime;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GeometryDashPlace.Web.Administration;

public sealed partial class EntityFrameworkAdministrationService(
    IDbContextFactory<GeometryDashPlaceDbContext> contextFactory,
    SiteOwnership siteOwnership,
    EventLifecycleNotifier lifecycleNotifier,
    EnvironmentAssetCatalog environmentAssets,
    LevelRealtimeService realtime)
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
                levelEvent.EndsAt,
                levelEvent.BackgroundKey,
                levelEvent.GroundKey))
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
            Width = normalized.Width,
            Height = normalized.Height,
            CooldownSeconds = normalized.CooldownSeconds,
            BackgroundKey = normalized.BackgroundKey,
            GroundKey = normalized.GroundKey,
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

        if (normalized.Width != entity.Width || normalized.Height != entity.Height)
        {
            throw Error(
                "event_dimensions_locked",
                "The grid dimensions cannot be changed after the event is created.",
                StatusCodes.Status409Conflict);
        }

        entity.Slug = normalized.Slug;
        entity.Name = normalized.Name;
        entity.Description = normalized.Description;
        entity.CooldownSeconds = normalized.CooldownSeconds;
        entity.BackgroundKey = normalized.BackgroundKey;
        entity.GroundKey = normalized.GroundKey;
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

    public async Task SetBannedAsync(
        Guid actorUserId,
        Guid userId,
        bool isBanned,
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

        if (isBanned && siteOwnership.IsOwner(user.Email))
        {
            throw Error(
                "site_owner", "The site owner cannot be banned.",
                StatusCodes.Status409Conflict);
        }

        if (isBanned && user.Id == actorUserId)
        {
            throw Error(
                "self_ban", "You cannot ban your own account.",
                StatusCodes.Status409Conflict);
        }

        user.IsBanned = isBanned;
        if (isBanned)
        {
            user.IsAdmin = false;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminPlacementHistory>> GetPlacementHistoryAsync(
        Guid actorUserId,
        Guid eventId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await RequireAdminAsync(context, actorUserId, cancellationToken);
        if (!await context.Events.AsNoTracking().AnyAsync(
                levelEvent => levelEvent.Id == eventId,
                cancellationToken))
        {
            throw Error(
                "event_not_found", "The event does not exist.",
                StatusCodes.Status404NotFound);
        }

        var history = await context.PlacementHistory
            .AsNoTracking()
            .Include(change => change.User)
            .Where(change => change.EventId == eventId)
            .OrderByDescending(change => change.Revision)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);
        return history.Select(change => new AdminPlacementHistory(
            change.Revision,
            change.Action,
            change.X,
            change.Y,
            change.SourceX,
            change.SourceY,
            change.NewObject?.Type ?? change.PreviousObject?.Type,
            change.UserId,
            change.User.DisplayName,
            change.PlacedAt)).ToArray();
    }

    public async Task<AdminModerationResult> RevertRevisionAsync(
        Guid actorUserId,
        Guid eventId,
        long revision,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await RequireAdminAsync(context, actorUserId, cancellationToken);
        var levelEvent = await RequireActiveEventAsync(
            context, eventId, cancellationToken);
        var change = await context.PlacementHistory
            .AsNoTracking()
            .SingleOrDefaultAsync(
                history => history.EventId == eventId && history.Revision == revision,
                cancellationToken) ?? throw Error(
                    "revision_not_found", "The requested revision does not exist.",
                    StatusCodes.Status404NotFound);
        var desired = DesiredStateBefore(change);
        var changes = await ApplyDesiredStateAsync(
            context,
            levelEvent,
            actorUserId,
            desired,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await PublishModerationReloadAsync(eventId, actorUserId, levelEvent.CurrentRevision);
        return new AdminModerationResult(
            eventId, levelEvent.CurrentRevision, changes);
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

    private AdminEventInput NormalizeAndValidate(AdminEventInput input)
    {
        var details = NormalizeAndValidateDetails(new AdminEventDetailsInput(
            input.Slug, input.Name, input.Description));
        var normalized = input with
        {
            Slug = details.Slug,
            Name = details.Name,
            Description = details.Description,
            BackgroundKey = input.BackgroundKey.Trim().ToLowerInvariant(),
            GroundKey = input.GroundKey.Trim().ToLowerInvariant(),
            StartsAt = input.StartsAt?.ToUniversalTime(),
            EndsAt = input.EndsAt?.ToUniversalTime()
        };

        if (normalized.CooldownSeconds is < 0 or > 86400)
        {
            throw Error(
                "invalid_cooldown", "The cooldown must be between 0 and 86400 seconds.",
                StatusCodes.Status400BadRequest);
        }

        if (normalized.Width < EventGridLimits.MinimumWidth ||
            normalized.Height < EventGridLimits.MinimumHeight)
        {
            throw Error(
                "invalid_dimensions",
                "The grid width and height must contain at least one cell.",
                StatusCodes.Status400BadRequest);
        }

        if (!environmentAssets.HasBackground(normalized.BackgroundKey))
        {
            throw Error(
                "invalid_background", "Choose an available background.",
                StatusCodes.Status400BadRequest);
        }

        if (!environmentAssets.HasGround(normalized.GroundKey))
        {
            throw Error(
                "invalid_ground", "Choose an available ground.",
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

    private static async Task<LevelEventEntity> RequireActiveEventAsync(
        GeometryDashPlaceDbContext context,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        var levelEvent = await context.Events.SingleOrDefaultAsync(
            candidate => candidate.Id == eventId,
            cancellationToken) ?? throw Error(
                "event_not_found", "The event does not exist.",
                StatusCodes.Status404NotFound);
        var now = DateTimeOffset.UtcNow;
        if (levelEvent.Status != "open" ||
            levelEvent.StartsAt is { } startsAt && startsAt > now ||
            levelEvent.EndsAt is { } endsAt && endsAt <= now)
        {
            throw Error(
                "event_not_open",
                "Placements can only be moderated while the event is ongoing.",
                StatusCodes.Status409Conflict);
        }

        return levelEvent;
    }

    private static Dictionary<(int X, int Y), LevelCell?> DesiredStateBefore(
        PlacementHistoryEntity change)
    {
        var desired = new Dictionary<(int X, int Y), LevelCell?>();
        var target = (change.X, change.Y);
        switch (change.Action)
        {
            case "place":
                desired[target] = null;
                break;
            case "replace":
            case "delete":
                desired[target] = RequiredHistoricalCell(change.PreviousObject);
                break;
            case "move":
                desired[target] = null;
                desired[RequiredSource(change)] = RequiredHistoricalCell(change.PreviousObject);
                break;
            case "move_replace":
                desired[target] = RequiredHistoricalCell(change.ReplacedObject);
                desired[RequiredSource(change)] = RequiredHistoricalCell(change.PreviousObject);
                break;
            default:
                throw Error(
                    "history_incomplete", "This revision cannot be reverted safely.",
                    StatusCodes.Status409Conflict);
        }

        return desired;
    }

    private static LevelCell RequiredHistoricalCell(LevelCell? cell) =>
        cell ?? throw Error(
            "history_incomplete", "This revision cannot be reverted safely.",
            StatusCodes.Status409Conflict);

    private static (int X, int Y) RequiredSource(PlacementHistoryEntity change) =>
        change.SourceX is { } sourceX && change.SourceY is { } sourceY
            ? (sourceX, sourceY)
            : throw Error(
                "history_incomplete", "This revision cannot be reverted safely.",
                StatusCodes.Status409Conflict);

    private static async Task<int> ApplyDesiredStateAsync(
        GeometryDashPlaceDbContext context,
        LevelEventEntity levelEvent,
        Guid actorUserId,
        Dictionary<(int X, int Y), LevelCell?> desired,
        CancellationToken cancellationToken)
    {
        var currentCells = await context.LevelCells
            .Include(cell => cell.Author)
            .Where(cell => cell.EventId == levelEvent.Id)
            .ToListAsync(cancellationToken);
        var currentByPosition = currentCells.ToDictionary(
            cell => (cell.X, cell.Y));
        var authorIds = desired.Values
            .OfType<LevelCell>()
            .Select(cell => cell.AuthorUserId)
            .Distinct()
            .ToList();
        var authorNames = await context.Users
            .AsNoTracking()
            .Where(user => authorIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken);
        if (authorNames.Count != authorIds.Count)
        {
            throw Error(
                "history_incomplete", "A historical object author no longer exists.",
                StatusCodes.Status409Conflict);
        }

        var now = DateTimeOffset.UtcNow;
        var changedCells = 0;
        foreach (var entry in desired.OrderBy(entry => entry.Key.X).ThenBy(entry => entry.Key.Y))
        {
            var position = entry.Key;
            var target = entry.Value;
            currentByPosition.TryGetValue(position, out var current);
            if (CellsMatch(current, target))
            {
                continue;
            }

            var previous = current is null
                ? null
                : ToLevelCell(current, current.Author.DisplayName);
            var revision = ++levelEvent.CurrentRevision;
            LevelCell? next = null;
            string action;
            if (target is null)
            {
                context.LevelCells.Remove(current!);
                action = "delete";
            }
            else
            {
                action = current is null ? "place" : "replace";
                current ??= new LevelCellEntity
                {
                    EventId = levelEvent.Id,
                    X = position.X,
                    Y = position.Y,
                    ObjectTypeKey = target.Type
                };
                if (context.Entry(current).State == EntityState.Detached)
                {
                    context.LevelCells.Add(current);
                }

                ApplyHistoricalCell(current, target, revision, now);
                next = ToLevelCell(current, authorNames[target.AuthorUserId]);
            }

            context.PlacementHistory.Add(new PlacementHistoryEntity
            {
                EventId = levelEvent.Id,
                Revision = revision,
                RequestId = Guid.NewGuid(),
                UserId = actorUserId,
                X = position.X,
                Y = position.Y,
                Action = action,
                PreviousObject = previous,
                NewObject = next,
                PlacedAt = now
            });
            changedCells++;
        }

        if (changedCells == 0)
        {
            throw Error(
                "no_changes", "The level already matches the requested state.",
                StatusCodes.Status409Conflict);
        }

        levelEvent.UpdatedAt = now;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Error(
                "event_changed", "The event changed concurrently. Reload and try again.",
                StatusCodes.Status409Conflict);
        }

        return changedCells;
    }

    private static bool CellsMatch(LevelCellEntity? current, LevelCell? target) =>
        current is null && target is null ||
        current is not null && target is not null &&
        current.ObjectTypeKey == target.Type &&
        (double)current.Rotation == target.Rotation &&
        (double)current.ScaleX == target.ScaleX &&
        (double)current.ScaleY == target.ScaleY &&
        current.ColorRed == target.Red &&
        current.ColorGreen == target.Green &&
        current.ColorBlue == target.Blue &&
        (current.DurationSeconds is null ? null : (double?)current.DurationSeconds) == target.Duration &&
        current.AuthorUserId == target.AuthorUserId;

    private static void ApplyHistoricalCell(
        LevelCellEntity entity,
        LevelCell source,
        long revision,
        DateTimeOffset now)
    {
        entity.ObjectTypeKey = source.Type;
        entity.Rotation = (decimal)source.Rotation;
        entity.ScaleX = (decimal)source.ScaleX;
        entity.ScaleY = (decimal)source.ScaleY;
        entity.ColorRed = source.Red is { } red ? (short)red : null;
        entity.ColorGreen = source.Green is { } green ? (short)green : null;
        entity.ColorBlue = source.Blue is { } blue ? (short)blue : null;
        entity.DurationSeconds = source.Duration is { } duration ? (decimal)duration : null;
        entity.AuthorUserId = source.AuthorUserId;
        entity.Revision = revision;
        entity.PlacedAt = now;
    }

    private static LevelCell ToLevelCell(LevelCellEntity cell, string author) => new(
        cell.X,
        cell.Y,
        cell.ObjectTypeKey,
        (double)cell.Rotation,
        (double)cell.ScaleX,
        (double)cell.ScaleY,
        cell.ColorRed,
        cell.ColorGreen,
        cell.ColorBlue,
        cell.DurationSeconds is { } duration ? (double)duration : null,
        cell.AuthorUserId,
        author,
        cell.Revision,
        cell.PlacedAt);

    private Task PublishModerationReloadAsync(
        Guid eventId,
        Guid actorUserId,
        long revision) => realtime.PublishAsync(new LevelChange(
            eventId,
            actorUserId,
            "moderation_restore",
            revision,
            0,
            0,
            null,
            null,
            null,
            null));

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
        levelEvent.EndsAt,
        levelEvent.BackgroundKey,
        levelEvent.GroundKey);

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
