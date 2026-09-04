using System.Data;
using GeometryDashPlace.Web.Data;
using GeometryDashPlace.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GeometryDashPlace.Web.Persistence;

public sealed class EntityFrameworkLevelRepository(
    IDbContextFactory<GeometryDashPlaceDbContext> contextFactory) : ILevelRepository
{
    private const double MinimumScale = 0.5;
    private const double MaximumScale = 2;

    public async Task<LevelState> LoadAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        var revision = await context.Events
            .AsNoTracking()
            .Where(levelEvent => levelEvent.Id == eventId)
            .Select(levelEvent => (long?)levelEvent.CurrentRevision)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw Error(
                "event_not_found", "The event does not exist.",
                StatusCodes.Status404NotFound);
        var cells = await context.LevelCells
            .AsNoTracking()
            .Include(cell => cell.Author)
            .Where(cell => cell.EventId == eventId)
            .OrderBy(cell => cell.X)
            .ThenBy(cell => cell.Y)
            .Select(cell => ToLevelCell(cell, cell.Author.DisplayName))
            .ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new LevelState(eventId, revision, cells);
    }

    public async Task<LevelCooldownState> GetCooldownAsync(
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await context.Events.AsNoTracking().AnyAsync(
                levelEvent => levelEvent.Id == eventId, cancellationToken))
        {
            throw Error(
                "event_not_found", "The event does not exist.",
                StatusCodes.Status404NotFound);
        }

        var nextPlacementAt = await context.UserEventStates
            .AsNoTracking()
            .Where(state => state.EventId == eventId && state.UserId == userId)
            .Select(state => (DateTimeOffset?)state.NextPlacementAt)
            .SingleOrDefaultAsync(cancellationToken);
        return new LevelCooldownState(DateTimeOffset.UtcNow, nextPlacementAt);
    }

    public async Task<LevelMutation> PlaceAsync(
        Guid eventId,
        Guid userId,
        int x,
        int y,
        PlaceLevelCellRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifiers(eventId, userId, request.RequestId);
        ValidateTypeRequired(request.Type);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var history = await FindHistoryAsync(context, request.RequestId, cancellationToken);
            if (history is not null)
            {
                return await ReplayPlacementAsync(
                    context, history, eventId, userId, x, y, request, cancellationToken);
            }

            var now = DateTimeOffset.UtcNow;
            var levelEvent = await RequireEventAsync(context, eventId, cancellationToken);
            ValidateEvent(levelEvent, now);
            ValidateCoordinates(x, y, levelEvent);
            var user = await RequireUserAsync(context, userId, cancellationToken);
            var objectType = await RequireObjectTypeAsync(context, request.Type, cancellationToken);
            ValidatePlacement(request, objectType);
            var userState = await RequireReadyStateAsync(
                context, eventId, userId, now, cancellationToken);
            var entity = await context.LevelCells
                .Include(cell => cell.Author)
                .SingleOrDefaultAsync(
                    cell => cell.EventId == eventId && cell.X == x && cell.Y == y,
                    cancellationToken);
            var previous = entity is null
                ? null
                : ToLevelCell(entity, entity.Author.DisplayName);
            var revision = ++levelEvent.CurrentRevision;
            var action = entity is null ? "place" : "replace";
            entity ??= CreateCell(eventId, x, y, userId, request);
            if (context.Entry(entity).State == EntityState.Detached)
            {
                context.LevelCells.Add(entity);
            }

            ApplyPlacement(entity, userId, request, revision, now);
            var cell = ToLevelCell(entity, user.DisplayName);
            context.PlacementHistory.Add(CreateHistory(
                eventId, userId, request.RequestId, x, y, action,
                revision, previous, cell));
            var nextPlacementAt = AdvanceCooldown(
                userState, now, levelEvent.CooldownSeconds);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new LevelMutation(action, revision, nextPlacementAt, cell);
        }
        catch (Exception exception) when (IsConcurrencyFailure(exception))
        {
            throw ConcurrentUpdate();
        }
    }

    public async Task<LevelMutation> DeleteAsync(
        Guid eventId,
        Guid userId,
        int x,
        int y,
        DeleteLevelCellRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifiers(eventId, userId, request.RequestId);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var history = await FindHistoryAsync(context, request.RequestId, cancellationToken);
            if (history is not null)
            {
                return await ReplayDeletionAsync(
                    context, history, eventId, userId, x, y, cancellationToken);
            }

            var now = DateTimeOffset.UtcNow;
            var levelEvent = await RequireEventAsync(context, eventId, cancellationToken);
            ValidateEvent(levelEvent, now);
            ValidateCoordinates(x, y, levelEvent);
            _ = await RequireUserAsync(context, userId, cancellationToken);
            var userState = await RequireReadyStateAsync(
                context, eventId, userId, now, cancellationToken);
            var entity = await context.LevelCells
                .Include(cell => cell.Author)
                .SingleOrDefaultAsync(
                    cell => cell.EventId == eventId && cell.X == x && cell.Y == y,
                    cancellationToken)
                ?? throw Error(
                    "cell_not_found", "There is no object in this cell.",
                    StatusCodes.Status404NotFound);
            var previous = ToLevelCell(entity, entity.Author.DisplayName);
            var revision = ++levelEvent.CurrentRevision;
            context.LevelCells.Remove(entity);
            context.PlacementHistory.Add(CreateHistory(
                eventId, userId, request.RequestId, x, y, "delete",
                revision, previous, null));
            var nextPlacementAt = AdvanceCooldown(
                userState, now, levelEvent.CooldownSeconds);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new LevelMutation("delete", revision, nextPlacementAt, null);
        }
        catch (Exception exception) when (IsConcurrencyFailure(exception))
        {
            throw ConcurrentUpdate();
        }
    }

    public async Task<LevelMutation> MoveAsync(
        Guid eventId,
        Guid userId,
        int sourceX,
        int sourceY,
        MoveLevelCellRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifiers(eventId, userId, request.RequestId);
        ValidateTypeRequired(request.Type);
        if (sourceX == request.TargetX && sourceY == request.TargetY)
        {
            throw Error(
                "same_move_cell", "The source and target cells must be different.",
                StatusCodes.Status400BadRequest);
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var history = await FindHistoryAsync(context, request.RequestId, cancellationToken);
            if (history is not null)
            {
                return await ReplayMoveAsync(
                    context, history, eventId, userId, sourceX, sourceY,
                    request, cancellationToken);
            }

            var now = DateTimeOffset.UtcNow;
            var levelEvent = await RequireEventAsync(context, eventId, cancellationToken);
            ValidateEvent(levelEvent, now);
            ValidateCoordinates(sourceX, sourceY, levelEvent);
            ValidateCoordinates(request.TargetX, request.TargetY, levelEvent);
            var user = await RequireUserAsync(context, userId, cancellationToken);
            var objectType = await RequireObjectTypeAsync(context, request.Type, cancellationToken);
            var placement = request.ToPlacement();
            ValidatePlacement(placement, objectType);
            var userState = await RequireReadyStateAsync(
                context, eventId, userId, now, cancellationToken);
            var source = await context.LevelCells
                .Include(cell => cell.Author)
                .SingleOrDefaultAsync(
                    cell => cell.EventId == eventId &&
                            cell.X == sourceX && cell.Y == sourceY,
                    cancellationToken)
                ?? throw Error(
                    "source_cell_not_found", "There is no object in the source cell.",
                    StatusCodes.Status404NotFound);
            var replaced = await context.LevelCells
                .Include(cell => cell.Author)
                .SingleOrDefaultAsync(
                    cell => cell.EventId == eventId &&
                            cell.X == request.TargetX && cell.Y == request.TargetY,
                    cancellationToken);
            var previous = ToLevelCell(source, source.Author.DisplayName);
            var replacedCell = replaced is null
                ? null
                : ToLevelCell(replaced, replaced.Author.DisplayName);
            var revision = ++levelEvent.CurrentRevision;
            context.LevelCells.Remove(source);
            LevelCellEntity target;
            if (replaced is null)
            {
                target = CreateCell(
                    eventId, request.TargetX, request.TargetY, userId, placement);
                context.LevelCells.Add(target);
            }
            else
            {
                target = replaced;
            }

            ApplyPlacement(target, userId, placement, revision, now);
            var cell = ToLevelCell(target, user.DisplayName);
            var action = replaced is null ? "move" : "move_replace";
            context.PlacementHistory.Add(CreateHistory(
                eventId, userId, request.RequestId,
                request.TargetX, request.TargetY, action, revision,
                previous, cell, sourceX, sourceY, replacedCell));
            var nextPlacementAt = AdvanceCooldown(
                userState, now, levelEvent.CooldownSeconds);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new LevelMutation(action, revision, nextPlacementAt, cell);
        }
        catch (Exception exception) when (IsConcurrencyFailure(exception))
        {
            throw ConcurrentUpdate();
        }
    }

    private static Task<PlacementHistoryEntity?> FindHistoryAsync(
        GeometryDashPlaceDbContext context,
        Guid requestId,
        CancellationToken cancellationToken) =>
        context.PlacementHistory
            .AsNoTracking()
            .SingleOrDefaultAsync(
                history => history.RequestId == requestId,
                cancellationToken);

    private static async Task<LevelEventEntity> RequireEventAsync(
        GeometryDashPlaceDbContext context,
        Guid eventId,
        CancellationToken cancellationToken) =>
        await context.Events.SingleOrDefaultAsync(
            levelEvent => levelEvent.Id == eventId,
            cancellationToken)
        ?? throw Error(
            "event_not_found", "The event does not exist.",
            StatusCodes.Status404NotFound);

    private static async Task<UserAccountEntity> RequireUserAsync(
        GeometryDashPlaceDbContext context,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            ?? throw Error(
                "user_not_found", "The user does not exist.",
                StatusCodes.Status404NotFound);
        if (user.IsBanned)
        {
            throw Error(
                "user_banned", "This user cannot modify the level.",
                StatusCodes.Status403Forbidden);
        }

        return user;
    }

    private static async Task<ObjectTypeEntity> RequireObjectTypeAsync(
        GeometryDashPlaceDbContext context,
        string type,
        CancellationToken cancellationToken) =>
        await context.ObjectTypes
            .AsNoTracking()
            .SingleOrDefaultAsync(objectType => objectType.Key == type, cancellationToken)
        ?? throw Error(
            "object_type_not_found", "The object type does not exist.",
            StatusCodes.Status400BadRequest);

    private static async Task<UserEventStateEntity> RequireReadyStateAsync(
        GeometryDashPlaceDbContext context,
        Guid eventId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var state = await context.UserEventStates.SingleOrDefaultAsync(
            candidate => candidate.EventId == eventId && candidate.UserId == userId,
            cancellationToken);
        if (state is null)
        {
            state = new UserEventStateEntity
            {
                EventId = eventId,
                UserId = userId,
                NextPlacementAt = DateTimeOffset.MinValue
            };
            context.UserEventStates.Add(state);
        }
        else if (state.NextPlacementAt > now)
        {
            throw Error(
                "placement_cooldown",
                "The placement cooldown has not expired.",
                StatusCodes.Status429TooManyRequests,
                state.NextPlacementAt);
        }

        return state;
    }

    private static LevelCellEntity CreateCell(
        Guid eventId,
        int x,
        int y,
        Guid userId,
        PlaceLevelCellRequest request) => new()
        {
            EventId = eventId,
            X = x,
            Y = y,
            ObjectTypeKey = request.Type,
            AuthorUserId = userId
        };

    private static void ApplyPlacement(
        LevelCellEntity cell,
        Guid userId,
        PlaceLevelCellRequest request,
        long revision,
        DateTimeOffset now)
    {
        cell.ObjectTypeKey = request.Type;
        cell.Rotation = (decimal)request.Rotation;
        cell.ScaleX = (decimal)request.ScaleX;
        cell.ScaleY = (decimal)request.ScaleY;
        cell.ColorRed = request.Red is { } red ? (short)red : null;
        cell.ColorGreen = request.Green is { } green ? (short)green : null;
        cell.ColorBlue = request.Blue is { } blue ? (short)blue : null;
        cell.DurationSeconds = request.Duration is { } duration ? (decimal)duration : null;
        cell.AuthorUserId = userId;
        cell.Revision = revision;
        cell.PlacedAt = now;
    }

    private static PlacementHistoryEntity CreateHistory(
        Guid eventId,
        Guid userId,
        Guid requestId,
        int x,
        int y,
        string action,
        long revision,
        LevelCell? previous,
        LevelCell? next,
        int? sourceX = null,
        int? sourceY = null,
        LevelCell? replaced = null) => new()
        {
            EventId = eventId,
            UserId = userId,
            RequestId = requestId,
            X = x,
            Y = y,
            SourceX = sourceX,
            SourceY = sourceY,
            Action = action,
            Revision = revision,
            PreviousObject = previous,
            NewObject = next,
            ReplacedObject = replaced,
            PlacedAt = DateTimeOffset.UtcNow
        };

    private static DateTimeOffset AdvanceCooldown(
        UserEventStateEntity state,
        DateTimeOffset now,
        int cooldownSeconds)
    {
        state.PlacementCount++;
        state.LastPlacementAt = now;
        state.NextPlacementAt = now.AddSeconds(cooldownSeconds);
        return state.NextPlacementAt;
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

    private static async Task<LevelMutation> ReplayPlacementAsync(
        GeometryDashPlaceDbContext context,
        PlacementHistoryEntity history,
        Guid eventId,
        Guid userId,
        int x,
        int y,
        PlaceLevelCellRequest request,
        CancellationToken cancellationToken)
    {
        var isSameMutation =
            history.EventId == eventId &&
            history.UserId == userId &&
            history.X == x &&
            history.Y == y &&
            history.Action is "place" or "replace" &&
            PlacementMatches(request, history.NewObject);
        EnsureSameRequest(isSameMutation);
        return new LevelMutation(
            history.Action,
            history.Revision,
            await ReadReplayCooldownAsync(
                context, eventId, userId, cancellationToken),
            history.NewObject,
            IsReplay: true);
    }

    private static async Task<LevelMutation> ReplayDeletionAsync(
        GeometryDashPlaceDbContext context,
        PlacementHistoryEntity history,
        Guid eventId,
        Guid userId,
        int x,
        int y,
        CancellationToken cancellationToken)
    {
        var isSameMutation =
            history.EventId == eventId &&
            history.UserId == userId &&
            history.X == x &&
            history.Y == y &&
            history.Action == "delete" &&
            history.NewObject is null;
        EnsureSameRequest(isSameMutation);
        return new LevelMutation(
            history.Action,
            history.Revision,
            await ReadReplayCooldownAsync(
                context, eventId, userId, cancellationToken),
            null,
            IsReplay: true);
    }

    private static async Task<LevelMutation> ReplayMoveAsync(
        GeometryDashPlaceDbContext context,
        PlacementHistoryEntity history,
        Guid eventId,
        Guid userId,
        int sourceX,
        int sourceY,
        MoveLevelCellRequest request,
        CancellationToken cancellationToken)
    {
        var isSameMutation =
            history.EventId == eventId &&
            history.UserId == userId &&
            history.X == request.TargetX &&
            history.Y == request.TargetY &&
            history.SourceX == sourceX &&
            history.SourceY == sourceY &&
            history.Action is "move" or "move_replace" &&
            PlacementMatches(request.ToPlacement(), history.NewObject);
        EnsureSameRequest(isSameMutation);
        return new LevelMutation(
            history.Action,
            history.Revision,
            await ReadReplayCooldownAsync(
                context, eventId, userId, cancellationToken),
            history.NewObject,
            IsReplay: true);
    }

    private static Task<DateTimeOffset?> ReadReplayCooldownAsync(
        GeometryDashPlaceDbContext context,
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken) =>
        context.UserEventStates
            .AsNoTracking()
            .Where(state => state.EventId == eventId && state.UserId == userId)
            .Select(state => (DateTimeOffset?)state.NextPlacementAt)
            .SingleOrDefaultAsync(cancellationToken);

    private static bool PlacementMatches(
        PlaceLevelCellRequest request,
        LevelCell? cell) =>
        cell is not null &&
        cell.Type == request.Type &&
        cell.Rotation == request.Rotation &&
        cell.ScaleX == request.ScaleX &&
        cell.ScaleY == request.ScaleY &&
        cell.Red == request.Red &&
        cell.Green == request.Green &&
        cell.Blue == request.Blue &&
        cell.Duration == request.Duration;

    private static void EnsureSameRequest(bool isSameMutation)
    {
        if (!isSameMutation)
        {
            throw Error(
                "request_id_conflict",
                "This requestId was already used for a different mutation.",
                StatusCodes.Status409Conflict);
        }
    }

    private static void ValidateIdentifiers(
        Guid eventId,
        Guid userId,
        Guid requestId)
    {
        if (eventId == Guid.Empty || userId == Guid.Empty || requestId == Guid.Empty)
        {
            throw Error(
                "invalid_identifier",
                "eventId, userId and requestId are required.",
                StatusCodes.Status400BadRequest);
        }
    }

    private static void ValidateTypeRequired(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw Error(
                "invalid_object_type", "The object type is required.",
                StatusCodes.Status400BadRequest);
        }
    }

    private static void ValidateEvent(
        LevelEventEntity levelEvent,
        DateTimeOffset now)
    {
        if (levelEvent.Status != "open" ||
            levelEvent.StartsAt is { } startsAt && startsAt > now ||
            levelEvent.EndsAt is { } endsAt && endsAt <= now)
        {
            throw Error(
                "event_not_open", "The event is not currently open.",
                StatusCodes.Status409Conflict);
        }
    }

    private static void ValidateCoordinates(
        int x,
        int y,
        LevelEventEntity levelEvent)
    {
        if (x < 0 || y < 0 || x >= levelEvent.Width || y >= levelEvent.Height)
        {
            throw Error(
                "cell_out_of_bounds", "The cell is outside the event grid.",
                StatusCodes.Status400BadRequest);
        }
    }

    private static void ValidatePlacement(
        PlaceLevelCellRequest request,
        ObjectTypeEntity objectType)
    {
        if (!objectType.IsActive)
        {
            throw Error(
                "object_type_inactive", "The object type is not available.",
                StatusCodes.Status400BadRequest);
        }

        if (!double.IsFinite(request.Rotation) ||
            request.Rotation < 0 ||
            request.Rotation >= 360)
        {
            throw Error(
                "invalid_rotation",
                "Rotation must be between 0 and 359.999 degrees.",
                StatusCodes.Status400BadRequest);
        }

        if (objectType.RotationMode == "none" && request.Rotation != 0 ||
            objectType.RotationMode == "quarter_turns" && request.Rotation % 90 != 0)
        {
            throw Error(
                "unsupported_rotation",
                "This object does not support the requested rotation.",
                StatusCodes.Status400BadRequest);
        }

        if (!double.IsFinite(request.ScaleX) ||
            !double.IsFinite(request.ScaleY) ||
            request.ScaleX < MinimumScale ||
            request.ScaleX > MaximumScale ||
            request.ScaleY < MinimumScale ||
            request.ScaleY > MaximumScale)
        {
            throw Error(
                "invalid_scale", "Scale X and Y must be between 0.5 and 2.",
                StatusCodes.Status400BadRequest);
        }

        if (!objectType.CanScale && (request.ScaleX != 1 || request.ScaleY != 1))
        {
            throw Error(
                "unsupported_scale", "This object cannot be scaled.",
                StatusCodes.Status400BadRequest);
        }

        ValidateColor(request, objectType);
        if (objectType.HasDurationSetting != request.Duration.HasValue ||
            request.Duration is { } duration &&
            (!double.IsFinite(duration) || duration < 0))
        {
            throw Error(
                "invalid_duration",
                "The duration is invalid for this object type.",
                StatusCodes.Status400BadRequest);
        }
    }

    private static void ValidateColor(
        PlaceLevelCellRequest request,
        ObjectTypeEntity objectType)
    {
        var hasCompleteColor =
            request.Red is not null &&
            request.Green is not null &&
            request.Blue is not null;
        var hasAnyColor =
            request.Red is not null ||
            request.Green is not null ||
            request.Blue is not null;
        if (objectType.HasColorSettings != hasCompleteColor ||
            hasAnyColor && !hasCompleteColor ||
            request.Red is < 0 or > 255 ||
            request.Green is < 0 or > 255 ||
            request.Blue is < 0 or > 255)
        {
            throw Error(
                "invalid_color",
                "This object requires either a complete RGB color or no color.",
                StatusCodes.Status400BadRequest);
        }
    }

    private static bool IsConcurrencyFailure(Exception exception)
    {
        if (exception is DbUpdateConcurrencyException)
        {
            return true;
        }

        if (exception is PostgresException postgresException)
        {
            return postgresException.SqlState is
                PostgresErrorCodes.SerializationFailure or
                PostgresErrorCodes.UniqueViolation;
        }

        return exception.InnerException is not null &&
            IsConcurrencyFailure(exception.InnerException);
    }

    private static LevelPersistenceException ConcurrentUpdate() => Error(
        "concurrent_update",
        "The level changed concurrently. Retry the request with the same requestId.",
        StatusCodes.Status409Conflict);

    private static LevelPersistenceException Error(
        string code,
        string message,
        int statusCode,
        DateTimeOffset? retryAt = null) =>
        new(code, message, statusCode, retryAt);
}
