using Npgsql;

namespace GeometryDashPlace.Web.Events;

public sealed class PostgresLevelEventRepository(NpgsqlDataSource dataSource) : ILevelEventRepository
{
    public async Task<LevelEvent?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, slug, name, description, width, height, cooldown_seconds, current_revision
            FROM events
            WHERE status = 'open'
              AND (starts_at IS NULL OR starts_at <= now())
              AND (ends_at IS NULL OR ends_at > now())
            ORDER BY starts_at DESC NULLS LAST, created_at DESC
            LIMIT 1
            """;

        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new LevelEvent(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt64(7));
    }
}
