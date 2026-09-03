namespace GeometryDashPlace.Web.Persistence;

public interface ILevelRepository
{
    Task<LevelState> LoadAsync(Guid eventId, CancellationToken cancellationToken = default);

    Task<LevelMutation> PlaceAsync(
        Guid eventId,
        int x,
        int y,
        PlaceLevelCellRequest request,
        CancellationToken cancellationToken = default);

    Task<LevelMutation> DeleteAsync(
        Guid eventId,
        int x,
        int y,
        DeleteLevelCellRequest request,
        CancellationToken cancellationToken = default);
}
