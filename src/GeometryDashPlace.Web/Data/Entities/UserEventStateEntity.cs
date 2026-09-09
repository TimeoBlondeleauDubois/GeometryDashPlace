namespace GeometryDashPlace.Web.Data.Entities;

public sealed class UserEventStateEntity
{
    public Guid EventId { get; set; }
    public Guid UserId { get; set; }
    public long PlacementCount { get; set; }
    public DateTimeOffset? LastPlacementAt { get; set; }
    public DateTimeOffset NextPlacementAt { get; set; } = DateTimeOffset.MinValue;
    public LevelEventEntity Event { get; set; } = default!;
    public UserAccountEntity User { get; set; } = default!;
}
