namespace GeometryDashPlace.Web.Events;

public interface ILevelEventRepository
{
    Task<LevelEvent?> GetCurrentAsync(CancellationToken cancellationToken = default);
}
