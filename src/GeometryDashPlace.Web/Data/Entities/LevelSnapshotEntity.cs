using GeometryDashPlace.Web.Persistence;

namespace GeometryDashPlace.Web.Data.Entities;

public sealed class LevelSnapshotEntity
{
    public long Id { get; set; }
    public Guid EventId { get; set; }
    public long Revision { get; set; }
    public required string SnapshotType { get; set; }
    public List<LevelCell> State { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public LevelEventEntity Event { get; set; } = default!;
}
