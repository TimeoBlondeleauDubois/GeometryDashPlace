namespace GeometryDashPlace.Web.Data.Entities;

public sealed class LevelEventEntity
{
    public Guid Id { get; set; }
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public int Width { get; set; } = 1024;
    public int Height { get; set; } = 32;
    public int CooldownSeconds { get; set; } = 60;
    public required string Status { get; set; }
    public long CurrentRevision { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<UserEventStateEntity> UserStates { get; set; } = [];
    public ICollection<LevelCellEntity> Cells { get; set; } = [];
    public ICollection<PlacementHistoryEntity> PlacementHistory { get; set; } = [];
    public ICollection<LevelSnapshotEntity> Snapshots { get; set; } = [];
}
