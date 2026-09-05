namespace GeometryDashPlace.Web.Events;

public interface ILevelEventRepository
{
    Task<LevelEvent?> GetCurrentAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LevelEvent>> GetPastAsync(
        CancellationToken cancellationToken = default);

    Task<LevelEvent?> GetBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default);
}
