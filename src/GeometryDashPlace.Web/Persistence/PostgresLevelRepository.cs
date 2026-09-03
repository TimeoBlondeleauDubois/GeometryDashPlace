using System.Data;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace GeometryDashPlace.Web.Persistence;

public sealed class PostgresLevelRepository(NpgsqlDataSource dataSource) : ILevelRepository
{
    private const double MinimumScale = 0.5;
    private const double MaximumScale = 2;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<LevelState> LoadAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var revision = await ReadEventRevisionAsync(connection, eventId, cancellationToken);
        var cells = new List<LevelCell>();
        await using var command = new NpgsqlCommand(
            CellSelectSql + " WHERE cell.event_id = @event_id ORDER BY cell.x, cell.y", connection);
        command.Parameters.AddWithValue("event_id", eventId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            cells.Add(ReadCell(reader));
        }

        return new LevelState(eventId, revision, cells);
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
        if (string.IsNullOrWhiteSpace(request.Type))
        {
            throw new LevelPersistenceException(
                "invalid_object_type", "The object type is required.",
                StatusCodes.Status400BadRequest);
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        try
        {
            if (await ReadReplayAsync(connection, transaction, eventId, x, y,
                    userId, request.RequestId, request, cancellationToken) is { } replay)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            var eventState = await LockEventAsync(connection, transaction, eventId, cancellationToken);
            ValidateEvent(eventState);
            ValidateCoordinates(x, y, eventState);

            var user = await ReadUserAsync(connection, transaction, userId, cancellationToken);
            var objectType = await ReadObjectTypeAsync(connection, transaction, request.Type, cancellationToken);
            ValidatePlacement(request, objectType);
            await EnsureCooldownExpiredAsync(connection, transaction, eventId,
                userId, cancellationToken);

            var previous = await ReadCellAsync(connection, transaction, eventId, x, y,
                lockRow: true, cancellationToken);
            var revision = await IncrementRevisionAsync(connection, transaction, eventId, cancellationToken);
            var placedAt = await UpsertCellAsync(connection, transaction, eventId, x, y,
                userId, request, revision, cancellationToken);
            var cell = new LevelCell(
                x, y, request.Type, request.Rotation, request.ScaleX, request.ScaleY,
                request.Red, request.Green, request.Blue, request.Duration,
                userId, user.DisplayName, revision, placedAt);
            var action = previous is null ? "place" : "replace";

            await InsertHistoryAsync(connection, transaction, eventId, userId,
                request.RequestId, x, y, action, previous, cell, revision, cancellationToken);
            var nextPlacementAt = await AdvanceCooldownAsync(connection, transaction,
                eventId, userId, eventState.CooldownSeconds, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new LevelMutation(action, revision, nextPlacementAt, cell);
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            throw new LevelPersistenceException(
                "concurrent_update",
                "The level changed concurrently. Retry the request with the same requestId.",
                StatusCodes.Status409Conflict);
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

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        try
        {
            if (await ReadReplayAsync(connection, transaction, eventId, x, y,
                    userId, request.RequestId, placement: null, cancellationToken) is { } replay)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            var eventState = await LockEventAsync(connection, transaction, eventId, cancellationToken);
            ValidateEvent(eventState);
            ValidateCoordinates(x, y, eventState);
            _ = await ReadUserAsync(connection, transaction, userId, cancellationToken);
            await EnsureCooldownExpiredAsync(connection, transaction, eventId,
                userId, cancellationToken);

            var previous = await ReadCellAsync(connection, transaction, eventId, x, y,
                lockRow: true, cancellationToken)
                ?? throw new LevelPersistenceException(
                    "cell_not_found", "There is no object in this cell.", StatusCodes.Status404NotFound);
            var revision = await IncrementRevisionAsync(connection, transaction, eventId, cancellationToken);

            await DeleteCellAsync(connection, transaction, eventId, x, y, cancellationToken);

            await InsertHistoryAsync(connection, transaction, eventId, userId,
                request.RequestId, x, y, "delete", previous, null, revision, cancellationToken);
            var nextPlacementAt = await AdvanceCooldownAsync(connection, transaction,
                eventId, userId, eventState.CooldownSeconds, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new LevelMutation("delete", revision, nextPlacementAt, null);
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            throw new LevelPersistenceException(
                "concurrent_update",
                "The level changed concurrently. Retry the request with the same requestId.",
                StatusCodes.Status409Conflict);
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
        if (string.IsNullOrWhiteSpace(request.Type))
        {
            throw new LevelPersistenceException(
                "invalid_object_type", "The object type is required.",
                StatusCodes.Status400BadRequest);
        }

        if (sourceX == request.TargetX && sourceY == request.TargetY)
        {
            throw new LevelPersistenceException(
                "same_move_cell", "The source and target cells must be different.",
                StatusCodes.Status400BadRequest);
        }

        var placement = request.ToPlacement();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        try
        {
            if (await ReadMoveReplayAsync(connection, transaction, eventId, userId,
                    sourceX, sourceY, request, cancellationToken) is { } replay)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            var eventState = await LockEventAsync(connection, transaction, eventId, cancellationToken);
            ValidateEvent(eventState);
            ValidateCoordinates(sourceX, sourceY, eventState);
            ValidateCoordinates(request.TargetX, request.TargetY, eventState);

            var user = await ReadUserAsync(connection, transaction, userId, cancellationToken);
            var objectType = await ReadObjectTypeAsync(
                connection, transaction, request.Type, cancellationToken);
            ValidatePlacement(placement, objectType);
            await EnsureCooldownExpiredAsync(
                connection, transaction, eventId, userId, cancellationToken);

            var source = await ReadCellAsync(
                connection, transaction, eventId, sourceX, sourceY,
                lockRow: true, cancellationToken)
                ?? throw new LevelPersistenceException(
                    "source_cell_not_found", "There is no object in the source cell.",
                    StatusCodes.Status404NotFound);
            var replaced = await ReadCellAsync(
                connection, transaction, eventId, request.TargetX, request.TargetY,
                lockRow: true, cancellationToken);
            var revision = await IncrementRevisionAsync(
                connection, transaction, eventId, cancellationToken);

            await DeleteCellAsync(
                connection, transaction, eventId, sourceX, sourceY, cancellationToken);
            var placedAt = await UpsertCellAsync(
                connection, transaction, eventId, request.TargetX, request.TargetY,
                userId, placement, revision, cancellationToken);
            var cell = new LevelCell(
                request.TargetX, request.TargetY, request.Type,
                request.Rotation, request.ScaleX, request.ScaleY,
                request.Red, request.Green, request.Blue, request.Duration,
                userId, user.DisplayName, revision, placedAt);
            var action = replaced is null ? "move" : "move_replace";

            await InsertHistoryAsync(
                connection, transaction, eventId, userId, request.RequestId,
                request.TargetX, request.TargetY, action, source, cell, revision,
                cancellationToken, sourceX, sourceY, replaced);
            var nextPlacementAt = await AdvanceCooldownAsync(
                connection, transaction, eventId, userId,
                eventState.CooldownSeconds, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new LevelMutation(action, revision, nextPlacementAt, cell);
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            throw new LevelPersistenceException(
                "concurrent_update",
                "The level changed concurrently. Retry the request with the same requestId.",
                StatusCodes.Status409Conflict);
        }
    }

    private static async Task<long> ReadEventRevisionAsync(
        NpgsqlConnection connection,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT current_revision FROM events WHERE id = @event_id", connection);
        command.Parameters.AddWithValue("event_id", eventId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long revision
            ? revision
            : throw new LevelPersistenceException(
                "event_not_found", "The event does not exist.", StatusCodes.Status404NotFound);
    }

    private static async Task<EventState> LockEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT width, height, cooldown_seconds, status, starts_at, ends_at, now()
            FROM events
            WHERE id = @event_id
            FOR UPDATE
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("event_id", eventId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new LevelPersistenceException(
                "event_not_found", "The event does not exist.", StatusCodes.Status404NotFound);
        }

        return new EventState(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetString(3),
            ReadNullableTimestamp(reader, 4),
            ReadNullableTimestamp(reader, 5),
            ReadTimestamp(reader, 6));
    }

    private static async Task<UserState> ReadUserAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT display_name, is_banned FROM users WHERE id = @user_id", connection, transaction);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new LevelPersistenceException(
                "user_not_found", "The user does not exist.", StatusCodes.Status404NotFound);
        }

        if (reader.GetBoolean(1))
        {
            throw new LevelPersistenceException(
                "user_banned", "This user cannot modify the level.", StatusCodes.Status403Forbidden);
        }

        return new UserState(reader.GetString(0));
    }

    private static async Task<ObjectTypeState> ReadObjectTypeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string type,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT rotation_mode, can_scale, has_color_settings, has_duration_setting, is_active
            FROM object_types
            WHERE key = @type
            """, connection, transaction);
        command.Parameters.AddWithValue("type", type);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new LevelPersistenceException(
                "object_type_not_found", "The object type does not exist.", StatusCodes.Status400BadRequest);
        }

        return new ObjectTypeState(
            reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2),
            reader.GetBoolean(3), reader.GetBoolean(4));
    }

    private static async Task EnsureCooldownExpiredAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using (var insertCommand = new NpgsqlCommand(
            """
            INSERT INTO user_event_states (event_id, user_id)
            VALUES (@event_id, @user_id)
            ON CONFLICT (event_id, user_id) DO NOTHING
            """, connection, transaction))
        {
            insertCommand.Parameters.AddWithValue("event_id", eventId);
            insertCommand.Parameters.AddWithValue("user_id", userId);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand(
            """
            SELECT next_placement_at > now(),
                   CASE WHEN next_placement_at = '-infinity' THEN NULL ELSE next_placement_at END
            FROM user_event_states
            WHERE event_id = @event_id AND user_id = @user_id
            FOR UPDATE
            """, connection, transaction);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        if (reader.GetBoolean(0))
        {
            var retryAt = ReadNullableTimestamp(reader, 1);
            throw new LevelPersistenceException(
                "placement_cooldown",
                "The placement cooldown has not expired.",
                StatusCodes.Status429TooManyRequests,
                retryAt);
        }
    }

    private static async Task<DateTimeOffset> AdvanceCooldownAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid eventId,
        Guid userId,
        int cooldownSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE user_event_states
            SET placement_count = placement_count + 1,
                last_placement_at = now(),
                next_placement_at = now() + (@cooldown_seconds * interval '1 second')
            WHERE event_id = @event_id AND user_id = @user_id
            RETURNING next_placement_at
            """, connection, transaction);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("cooldown_seconds", cooldownSeconds);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return ToTimestamp((DateTime)result!);
    }

    private static async Task<long> IncrementRevisionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE events
            SET current_revision = current_revision + 1
            WHERE id = @event_id
            RETURNING current_revision
            """, connection, transaction);
        command.Parameters.AddWithValue("event_id", eventId);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<DateTimeOffset> UpsertCellAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid eventId,
        int x,
        int y,
        Guid userId,
        PlaceLevelCellRequest request,
        long revision,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO level_cells
                (event_id, x, y, object_type_key, rotation, scale_x, scale_y,
                 color_red, color_green, color_blue, duration_seconds,
                 author_user_id, placed_at, revision)
            VALUES
                (@event_id, @x, @y, @type, @rotation, @scale_x, @scale_y,
                 @red, @green, @blue, @duration,
                 @user_id, now(), @revision)
            ON CONFLICT (event_id, x, y) DO UPDATE SET
                object_type_key = EXCLUDED.object_type_key,
                rotation = EXCLUDED.rotation,
                scale_x = EXCLUDED.scale_x,
                scale_y = EXCLUDED.scale_y,
                color_red = EXCLUDED.color_red,
                color_green = EXCLUDED.color_green,
                color_blue = EXCLUDED.color_blue,
                duration_seconds = EXCLUDED.duration_seconds,
                author_user_id = EXCLUDED.author_user_id,
                placed_at = EXCLUDED.placed_at,
                revision = EXCLUDED.revision
            RETURNING placed_at
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("x", x);
        command.Parameters.AddWithValue("y", y);
        command.Parameters.AddWithValue("type", request.Type);
        command.Parameters.AddWithValue("rotation", NpgsqlDbType.Numeric, (decimal)request.Rotation);
        command.Parameters.AddWithValue("scale_x", NpgsqlDbType.Numeric, (decimal)request.ScaleX);
        command.Parameters.AddWithValue("scale_y", NpgsqlDbType.Numeric, (decimal)request.ScaleY);
        AddNullableSmallInt(command, "red", request.Red);
        AddNullableSmallInt(command, "green", request.Green);
        AddNullableSmallInt(command, "blue", request.Blue);
        AddNullableNumeric(command, "duration", request.Duration);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("revision", revision);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return ToTimestamp((DateTime)result!);
    }

    private static async Task DeleteCellAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid eventId,
        int x,
        int y,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "DELETE FROM level_cells WHERE event_id = @event_id AND x = @x AND y = @y",
            connection, transaction);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("x", x);
        command.Parameters.AddWithValue("y", y);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertHistoryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid eventId,
        Guid userId,
        Guid requestId,
        int x,
        int y,
        string action,
        LevelCell? previous,
        LevelCell? next,
        long revision,
        CancellationToken cancellationToken,
        int? sourceX = null,
        int? sourceY = null,
        LevelCell? replaced = null)
    {
        const string sql = """
            INSERT INTO placement_history
                (event_id, revision, request_id, user_id, x, y, source_x, source_y,
                 action, previous_object, new_object, replaced_object)
            VALUES
                (@event_id, @revision, @request_id, @user_id, @x, @y, @source_x, @source_y,
                 @action, @previous_object, @new_object, @replaced_object)
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("revision", revision);
        command.Parameters.AddWithValue("request_id", requestId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("x", x);
        command.Parameters.AddWithValue("y", y);
        AddNullableInteger(command, "source_x", sourceX);
        AddNullableInteger(command, "source_y", sourceY);
        command.Parameters.AddWithValue("action", action);
        AddNullableJson(command, "previous_object", previous);
        AddNullableJson(command, "new_object", next);
        AddNullableJson(command, "replaced_object", replaced);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<LevelMutation?> ReadMoveReplayAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid eventId,
        Guid userId,
        int sourceX,
        int sourceY,
        MoveLevelCellRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT event_id, user_id, x, y, source_x, source_y, action, revision, new_object
            FROM placement_history
            WHERE request_id = @request_id
            """, connection, transaction);
        command.Parameters.AddWithValue("request_id", request.RequestId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var action = reader.GetString(6);
        var persistedCell = reader.IsDBNull(8)
            ? null
            : JsonSerializer.Deserialize<LevelCell>(reader.GetString(8), JsonOptions);
        if (reader.GetGuid(0) != eventId || reader.GetGuid(1) != userId ||
            reader.GetInt32(2) != request.TargetX || reader.GetInt32(3) != request.TargetY ||
            reader.IsDBNull(4) || reader.GetInt32(4) != sourceX ||
            reader.IsDBNull(5) || reader.GetInt32(5) != sourceY ||
            action is not ("move" or "move_replace") ||
            !PlacementMatches(request.ToPlacement(), persistedCell))
        {
            throw new LevelPersistenceException(
                "request_id_conflict",
                "This requestId was already used for a different mutation.",
                StatusCodes.Status409Conflict);
        }

        return new LevelMutation(action, reader.GetInt64(7), null, persistedCell, IsReplay: true);
    }

    private static async Task<LevelMutation?> ReadReplayAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid eventId,
        int x,
        int y,
        Guid userId,
        Guid requestId,
        PlaceLevelCellRequest? placement,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT event_id, x, y, user_id, action, revision, new_object
            FROM placement_history
            WHERE request_id = @request_id
            """, connection, transaction);
        command.Parameters.AddWithValue("request_id", requestId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var action = reader.GetString(4);
        var persistedCell = reader.IsDBNull(6)
            ? null
            : JsonSerializer.Deserialize<LevelCell>(reader.GetString(6), JsonOptions);
        var isSameMutation = placement is null
            ? action == "delete" && persistedCell is null
            : action is "place" or "replace" && PlacementMatches(placement, persistedCell);

        if (reader.GetGuid(0) != eventId || reader.GetInt32(1) != x || reader.GetInt32(2) != y ||
            reader.GetGuid(3) != userId || !isSameMutation)
        {
            throw new LevelPersistenceException(
                "request_id_conflict",
                "This requestId was already used for a different mutation.",
                StatusCodes.Status409Conflict);
        }

        return new LevelMutation(action, reader.GetInt64(5), null, persistedCell, IsReplay: true);
    }

    private static bool PlacementMatches(PlaceLevelCellRequest request, LevelCell? cell) =>
        cell is not null &&
        cell.Type == request.Type &&
        cell.Rotation == request.Rotation &&
        cell.ScaleX == request.ScaleX &&
        cell.ScaleY == request.ScaleY &&
        cell.Red == request.Red &&
        cell.Green == request.Green &&
        cell.Blue == request.Blue &&
        cell.Duration == request.Duration;

    private static async Task<LevelCell?> ReadCellAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid eventId,
        int x,
        int y,
        bool lockRow,
        CancellationToken cancellationToken)
    {
        var suffix = lockRow ? " FOR UPDATE OF cell" : string.Empty;
        await using var command = new NpgsqlCommand(
            CellSelectSql + " WHERE cell.event_id = @event_id AND cell.x = @x AND cell.y = @y" + suffix,
            connection, transaction);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("x", x);
        command.Parameters.AddWithValue("y", y);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCell(reader) : null;
    }

    private static LevelCell ReadCell(NpgsqlDataReader reader) => new(
        reader.GetInt32(0),
        reader.GetInt32(1),
        reader.GetString(2),
        (double)reader.GetDecimal(3),
        (double)reader.GetDecimal(4),
        (double)reader.GetDecimal(5),
        reader.IsDBNull(6) ? null : reader.GetInt16(6),
        reader.IsDBNull(7) ? null : reader.GetInt16(7),
        reader.IsDBNull(8) ? null : reader.GetInt16(8),
        reader.IsDBNull(9) ? null : (double)reader.GetDecimal(9),
        reader.GetGuid(10),
        reader.GetString(11),
        reader.GetInt64(12),
        ReadTimestamp(reader, 13));

    private static void ValidateIdentifiers(Guid eventId, Guid userId, Guid requestId)
    {
        if (eventId == Guid.Empty || userId == Guid.Empty || requestId == Guid.Empty)
        {
            throw new LevelPersistenceException(
                "invalid_identifier", "eventId, userId and requestId are required.",
                StatusCodes.Status400BadRequest);
        }
    }

    private static void ValidateEvent(EventState eventState)
    {
        if (eventState.Status != "open" ||
            eventState.StartsAt is { } startsAt && startsAt > eventState.Now ||
            eventState.EndsAt is { } endsAt && endsAt <= eventState.Now)
        {
            throw new LevelPersistenceException(
                "event_not_open", "The event is not currently open.", StatusCodes.Status409Conflict);
        }
    }

    private static void ValidateCoordinates(int x, int y, EventState eventState)
    {
        if (x < 0 || y < 0 || x >= eventState.Width || y >= eventState.Height)
        {
            throw new LevelPersistenceException(
                "cell_out_of_bounds", "The cell is outside the event grid.", StatusCodes.Status400BadRequest);
        }
    }

    private static void ValidatePlacement(PlaceLevelCellRequest request, ObjectTypeState objectType)
    {
        if (!objectType.IsActive)
        {
            throw new LevelPersistenceException(
                "object_type_inactive", "The object type is not available.", StatusCodes.Status400BadRequest);
        }

        if (!double.IsFinite(request.Rotation) || request.Rotation < 0 || request.Rotation >= 360)
        {
            throw new LevelPersistenceException(
                "invalid_rotation", "Rotation must be between 0 and 359.999 degrees.",
                StatusCodes.Status400BadRequest);
        }

        if (objectType.RotationMode == "none" && request.Rotation != 0 ||
            objectType.RotationMode == "quarter_turns" && request.Rotation % 90 != 0)
        {
            throw new LevelPersistenceException(
                "unsupported_rotation", "This object does not support the requested rotation.",
                StatusCodes.Status400BadRequest);
        }

        if (!double.IsFinite(request.ScaleX) || !double.IsFinite(request.ScaleY) ||
            request.ScaleX < MinimumScale || request.ScaleX > MaximumScale ||
            request.ScaleY < MinimumScale || request.ScaleY > MaximumScale)
        {
            throw new LevelPersistenceException(
                "invalid_scale", "Scale X and Y must be between 0.5 and 2.",
                StatusCodes.Status400BadRequest);
        }

        if (!objectType.CanScale && (request.ScaleX != 1 || request.ScaleY != 1))
        {
            throw new LevelPersistenceException(
                "unsupported_scale", "This object cannot be scaled.",
                StatusCodes.Status400BadRequest);
        }

        var hasCompleteColor = request.Red is not null && request.Green is not null && request.Blue is not null;
        var hasAnyColor = request.Red is not null || request.Green is not null || request.Blue is not null;
        if (objectType.HasColorSettings != hasCompleteColor || hasAnyColor && !hasCompleteColor ||
            request.Red is < 0 or > 255 || request.Green is < 0 or > 255 || request.Blue is < 0 or > 255)
        {
            throw new LevelPersistenceException(
                "invalid_color", "This object requires either a complete RGB color or no color.",
                StatusCodes.Status400BadRequest);
        }

        if (objectType.HasDurationSetting != request.Duration.HasValue ||
            request.Duration is { } duration && (!double.IsFinite(duration) || duration < 0))
        {
            throw new LevelPersistenceException(
                "invalid_duration", "The duration is invalid for this object type.",
                StatusCodes.Status400BadRequest);
        }
    }

    private static void AddNullableSmallInt(NpgsqlCommand command, string name, int? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Smallint);
        parameter.Value = value is { } number ? (short)number : DBNull.Value;
    }

    private static void AddNullableNumeric(NpgsqlCommand command, string name, double? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Numeric);
        parameter.Value = value is { } number ? (decimal)number : DBNull.Value;
    }

    private static void AddNullableInteger(NpgsqlCommand command, string name, int? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Integer);
        parameter.Value = value is { } number ? number : DBNull.Value;
    }

    private static void AddNullableJson(NpgsqlCommand command, string name, LevelCell? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Jsonb);
        parameter.Value = value is null ? DBNull.Value : JsonSerializer.Serialize(value, JsonOptions);
    }

    private static DateTimeOffset ReadTimestamp(NpgsqlDataReader reader, int ordinal) =>
        ToTimestamp(reader.GetDateTime(ordinal));

    private static DateTimeOffset? ReadNullableTimestamp(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadTimestamp(reader, ordinal);

    private static DateTimeOffset ToTimestamp(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private const string CellSelectSql = """
        SELECT cell.x,
               cell.y,
               cell.object_type_key,
               cell.rotation,
               cell.scale_x,
               cell.scale_y,
               cell.color_red,
               cell.color_green,
               cell.color_blue,
               cell.duration_seconds,
               cell.author_user_id,
               user_account.display_name,
               cell.revision,
               cell.placed_at
        FROM level_cells AS cell
        JOIN users AS user_account ON user_account.id = cell.author_user_id
        """;

    private sealed record EventState(
        int Width,
        int Height,
        int CooldownSeconds,
        string Status,
        DateTimeOffset? StartsAt,
        DateTimeOffset? EndsAt,
        DateTimeOffset Now);

    private sealed record UserState(string DisplayName);
    private sealed record ObjectTypeState(
        string RotationMode,
        bool CanScale,
        bool HasColorSettings,
        bool HasDurationSetting,
        bool IsActive);
}
