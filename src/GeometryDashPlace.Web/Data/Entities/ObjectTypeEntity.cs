namespace GeometryDashPlace.Web.Data.Entities;

public sealed class ObjectTypeEntity
{
    public required string Key { get; set; }
    public required string DisplayName { get; set; }
    public required string Category { get; set; }
    public int? GeometryDashObjectId { get; set; }
    public decimal YOffset { get; set; }
    public required string RotationMode { get; set; }
    public bool CanScale { get; set; }
    public bool HasColorSettings { get; set; }
    public bool HasDurationSetting { get; set; }
    public string? AssetPath { get; set; }
    public bool IsActive { get; set; }
    public ICollection<LevelCellEntity> Cells { get; set; } = [];
}
