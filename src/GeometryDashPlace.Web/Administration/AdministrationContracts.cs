using GeometryDashPlace.Web.Events;

namespace GeometryDashPlace.Web.Administration;

public sealed record AdminUser(
    Guid Id,
    string Email,
    string DisplayName,
    bool IsAdmin,
    bool IsSiteOwner,
    bool IsBanned,
    DateTimeOffset? LastLoginAt);

public sealed record AdminEventInput(
    string Slug,
    string Name,
    string? Description,
    int CooldownSeconds,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    string BackgroundKey = "background-01",
    string GroundKey = "ground-01");

public sealed record AdminEventDetailsInput(
    string Slug,
    string Name,
    string? Description);

public sealed record AdminPlacementHistory(
    long Revision,
    string Action,
    int X,
    int Y,
    int? SourceX,
    int? SourceY,
    string? ObjectType,
    Guid UserId,
    string UserDisplayName,
    DateTimeOffset CreatedAt);

public sealed record AdminModerationResult(
    Guid EventId,
    long Revision,
    int ChangedCells);

public interface IAdministrationService
{
    Task<bool> IsAdminAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdminUser>> GetUsersAsync(
        Guid actorUserId,
        string? search = null,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LevelEvent>> GetEventsAsync(Guid actorUserId, CancellationToken cancellationToken = default);
    Task<LevelEvent> CreateEventAsync(Guid actorUserId, AdminEventInput input, CancellationToken cancellationToken = default);
    Task<LevelEvent> UpdateEventAsync(Guid actorUserId, Guid eventId, AdminEventInput input, CancellationToken cancellationToken = default);
    Task<LevelEvent> UpdateCompletedEventDetailsAsync(
        Guid actorUserId,
        Guid eventId,
        AdminEventDetailsInput input,
        CancellationToken cancellationToken = default);
    Task SetAdminAsync(Guid actorUserId, Guid userId, bool isAdmin, CancellationToken cancellationToken = default);
    Task SetBannedAsync(Guid actorUserId, Guid userId, bool isBanned, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdminPlacementHistory>> GetPlacementHistoryAsync(
        Guid actorUserId,
        Guid eventId,
        int limit = 100,
        CancellationToken cancellationToken = default);
    Task<AdminModerationResult> RevertRevisionAsync(
        Guid actorUserId,
        Guid eventId,
        long revision,
        CancellationToken cancellationToken = default);
}

public interface IEventLifecycleService
{
    Task<int> CloseExpiredEventsAsync(CancellationToken cancellationToken = default);
}

public sealed class AdministrationException(
    string code,
    string message,
    int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
