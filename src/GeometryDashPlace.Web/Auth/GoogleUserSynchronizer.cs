using Npgsql;

namespace GeometryDashPlace.Web.Auth;

public sealed class GoogleUserSynchronizer(NpgsqlDataSource dataSource)
{
    public async Task<GoogleUser> SynchronizeAsync(
        string subject,
        string email,
        string displayName,
        string? avatarUrl,
        bool isEmailVerified,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO users
                (google_subject, email, display_name, avatar_url, is_email_verified, last_login_at)
            VALUES
                (@subject, @email, @display_name, @avatar_url, @is_email_verified, now())
            ON CONFLICT (google_subject) DO UPDATE SET
                email = EXCLUDED.email,
                display_name = EXCLUDED.display_name,
                avatar_url = EXCLUDED.avatar_url,
                is_email_verified = EXCLUDED.is_email_verified,
                last_login_at = now()
            RETURNING id, display_name, is_banned
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("subject", subject);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("display_name", displayName);
        command.Parameters.AddWithValue("avatar_url", (object?)avatarUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("is_email_verified", isEmailVerified);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new GoogleUser(reader.GetGuid(0), reader.GetString(1), reader.GetBoolean(2));
    }
}

public sealed record GoogleUser(Guid Id, string DisplayName, bool IsBanned);
