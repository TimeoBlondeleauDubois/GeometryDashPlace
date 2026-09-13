using GeometryDashPlace.Web.Profiles;

namespace GeometryDashPlace.Web.Persistence;

public sealed class BadgeAwardingLevelRepository(
    EntityFrameworkLevelRepository inner,
    IPlayerBadgeService badges,
    PlayerBadgeNotificationDispatcher notifications) : ILevelRepository
{
    public Task<LevelState> LoadAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        inner.LoadAsync(eventId, cancellationToken);

    public Task<LevelState> LoadRevisionAsync(
        Guid eventId,
        long revision,
        CancellationToken cancellationToken = default) =>
        inner.LoadRevisionAsync(eventId, revision, cancellationToken);

    public Task<IReadOnlyList<LevelRevisionDetails>> LoadRevisionHistoryAsync(
        Guid eventId,
        CancellationToken cancellationToken = default) =>
        inner.LoadRevisionHistoryAsync(eventId, cancellationToken);

    public Task<LevelCooldownState> GetCooldownAsync(
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        inner.GetCooldownAsync(eventId, userId, cancellationToken);

    public async Task<LevelMutation> PlaceAsync(
        Guid eventId,
        Guid userId,
        int x,
        int y,
        PlaceLevelCellRequest request,
        CancellationToken cancellationToken = default)
    {
        var mutation = await inner.PlaceAsync(eventId, userId, x, y, request, cancellationToken);
        await AwardAndNotifyAsync(userId, cancellationToken);
        return mutation;
    }

    public async Task<LevelMutation> DeleteAsync(
        Guid eventId,
        Guid userId,
        int x,
        int y,
        DeleteLevelCellRequest request,
        CancellationToken cancellationToken = default)
    {
        var mutation = await inner.DeleteAsync(eventId, userId, x, y, request, cancellationToken);
        await AwardAndNotifyAsync(userId, cancellationToken);
        return mutation;
    }

    public async Task<LevelMutation> MoveAsync(
        Guid eventId,
        Guid userId,
        int sourceX,
        int sourceY,
        MoveLevelCellRequest request,
        CancellationToken cancellationToken = default)
    {
        var mutation = await inner.MoveAsync(
            eventId, userId, sourceX, sourceY, request, cancellationToken);
        await AwardAndNotifyAsync(userId, cancellationToken);
        return mutation;
    }

    private async Task AwardAndNotifyAsync(Guid userId, CancellationToken cancellationToken)
    {
        var unlocked = await badges.GetPendingAsync(userId, cancellationToken);
        await notifications.PublishAsync(unlocked);
    }
}
