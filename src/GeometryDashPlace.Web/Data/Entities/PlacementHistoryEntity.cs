using GeometryDashPlace.Web.Persistence;

namespace GeometryDashPlace.Web.Data.Entities;

public sealed class PlacementHistoryEntity
{
    public long Id { get; set; }
    public Guid EventId { get; set; }
    public long Revision { get; set; }
    public Guid RequestId { get; set; }
    public Guid UserId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int? SourceX { get; set; }
    public int? SourceY { get; set; }
    public required string Action { get; set; }
    public LevelCell? PreviousObject { get; set; }
    public LevelCell? NewObject { get; set; }
    public LevelCell? ReplacedObject { get; set; }
    public DateTimeOffset PlacedAt { get; set; }
    public LevelEventEntity Event { get; set; } = default!;
    public UserAccountEntity User { get; set; } = default!;
}
