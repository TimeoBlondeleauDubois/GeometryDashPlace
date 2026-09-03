namespace GeometryDashPlace.Web.Persistence;

public sealed class LevelPersistenceException(
    string code,
    string message,
    int statusCode,
    DateTimeOffset? retryAt = null) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public DateTimeOffset? RetryAt { get; } = retryAt;
}
