namespace GeometryDashPlace.Web.Persistence;

public interface ILevelRepository
{
    Task<LevelState> LoadAsync(Guid eventId, CancellationToken cancellationToken = default);

    Task<LevelCooldownState> GetCooldownAsync(
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<LevelMutation> PlaceAsync(
        Guid eventId,
        Guid userId,
        int x,
        int y,
        PlaceLevelCellRequest request,
        CancellationToken cancellationToken = default);

    Task<LevelMutation> DeleteAsync(
        Guid eventId,
        Guid userId,
        int x,
        int y,
        DeleteLevelCellRequest request,
        CancellationToken cancellationToken = default);

    Task<LevelMutation> MoveAsync(
        Guid eventId,
        Guid userId,
        int sourceX,
        int sourceY,
        MoveLevelCellRequest request,
        CancellationToken cancellationToken = default);
}
