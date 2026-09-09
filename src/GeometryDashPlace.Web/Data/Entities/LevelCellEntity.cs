namespace GeometryDashPlace.Web.Data.Entities;

public sealed class LevelCellEntity
{
    public Guid EventId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public required string ObjectTypeKey { get; set; }
    public decimal Rotation { get; set; }
    public decimal ScaleX { get; set; } = 1;
    public decimal ScaleY { get; set; } = 1;
    public short? ColorRed { get; set; }
    public short? ColorGreen { get; set; }
    public short? ColorBlue { get; set; }
    public decimal? DurationSeconds { get; set; }
    public Guid AuthorUserId { get; set; }
    public DateTimeOffset PlacedAt { get; set; }
    public long Revision { get; set; }
    public LevelEventEntity Event { get; set; } = default!;
    public ObjectTypeEntity ObjectType { get; set; } = default!;
    public UserAccountEntity Author { get; set; } = default!;
}
